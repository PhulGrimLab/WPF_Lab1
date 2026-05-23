using System.Diagnostics;

namespace Wpf.Lib.RTOS
{
    /// <summary>
    /// RTOS 스타일 태스크를 주기적으로 실행하고 상태 스냅샷을 발행하는 스케줄러 엔진입니다.
    /// </summary>
    public sealed class SchedulerService : IDisposable, IAsyncDisposable
    {
        private readonly object _syncRoot = new();
        private readonly object _stateSyncRoot = new();
        private readonly List<IScheduledTask> _tasks = [];
        private readonly List<IScheduledTask> _executionBuffer = [];
        private readonly Dictionary<IScheduledTask, TaskRuntimeInfo> _runtimeInfos = [];
        private readonly Dictionary<IScheduledTask, DateTimeOffset> _disabledRevisitAt = [];
        private readonly PriorityQueue<IScheduledTask, DueTaskPriority> _dueTaskQueue = new(DueTaskPriorityComparer.Instance);
        private static readonly AsyncLocal<bool> s_isSchedulerExecution = new();
        private readonly TimeSpan _tickInterval;
        private readonly TimeSpan _snapshotInterval;
        private readonly TimeSpan? _timeQuantum;
        private CancellationTokenSource? _cts;
        private Task? _runTask;
        private DateTimeOffset _nextSnapshotAt = DateTimeOffset.MinValue;
        private SchedulerRunState _state = SchedulerRunState.Stopped;
        private long _dueSequence;
        private bool _isDueQueueDirty;

        /// <summary>
        /// 스케줄러를 생성합니다.
        /// </summary>
        /// <param name="tickInterval">최대 폴링 간격입니다. 기본값은 10ms입니다.</param>
        /// <param name="snapshotInterval">SnapshotChanged 이벤트 발행 간격입니다. 기본값은 100ms입니다.</param>
        /// <param name="timeQuantum">선점 근사를 위한 실행 시간 조각입니다. null이면 비활성입니다.</param>
        public SchedulerService(TimeSpan? tickInterval = null, TimeSpan? snapshotInterval = null, TimeSpan? timeQuantum = null)
        {
            _tickInterval = tickInterval ?? TimeSpan.FromMilliseconds(10);
            _snapshotInterval = snapshotInterval ?? TimeSpan.FromMilliseconds(100);
            _timeQuantum = timeQuantum;

            if (_tickInterval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(tickInterval), "Tick interval must be greater than zero.");
            }

            if (_snapshotInterval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(snapshotInterval), "Snapshot interval must be greater than zero.");
            }

            if (_timeQuantum is not null && _timeQuantum <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeQuantum), "Time quantum must be greater than zero.");
            }
        }

        /// <summary>
        /// 스냅샷이 갱신될 때 발생합니다.
        /// </summary>
        public event EventHandler<SchedulerSnapshot>? SnapshotChanged;

        /// <summary>
        /// 태스크 실행 또는 이벤트 핸들러 처리 중 예외가 발생할 때 보고합니다.
        /// </summary>
        public event EventHandler<Exception>? SchedulerError;

        /// <summary>
        /// 최근 실행 로그를 보관하는 trace 버퍼입니다.
        /// </summary>
        public RtosTraceLog TraceLog { get; } = new();

        /// <summary>
        /// 스케줄러가 현재 실행 중인지 나타냅니다.
        /// </summary>
        public bool IsRunning
        {
            get
            {
                lock (_stateSyncRoot)
                {
                    return _state == SchedulerRunState.Running;
                }
            }
        }

        /// <summary>
        /// 태스크를 등록합니다.
        /// </summary>
        /// <param name="task">등록할 태스크입니다.</param>
        public void Register(IScheduledTask task)
        {
            ThrowIfDisposed();

            ArgumentNullException.ThrowIfNull(task);

            lock (_syncRoot)
            {
                if (_runtimeInfos.ContainsKey(task))
                {
                    return;
                }

                _tasks.Add(task);
                _runtimeInfos[task] = new TaskRuntimeInfo();
                EnqueueTaskUnsafe(task);
            }

            TraceLog.Add("Scheduler", "Task registered.", GetTaskName(task));
            PublishSnapshot(force: true);
        }

        /// <summary>
        /// 태스크 등록을 해제합니다.
        /// </summary>
        /// <param name="task">제거할 태스크입니다.</param>
        /// <returns>성공적으로 제거되면 true입니다.</returns>
        public bool Unregister(IScheduledTask task)
        {
            ThrowIfDisposed();

            ArgumentNullException.ThrowIfNull(task);

            var removed = false;

            lock (_syncRoot)
            {
                removed = _tasks.Remove(task);
                _runtimeInfos.Remove(task);
                _disabledRevisitAt.Remove(task);
                if (removed)
                {
                    _isDueQueueDirty = true;
                }
            }

            if (removed)
            {
                TraceLog.Add("Scheduler", "Task unregistered.", GetTaskName(task));
                PublishSnapshot(force: true);
            }

            return removed;
        }

        /// <summary>
        /// 등록된 모든 태스크와 실행 통계를 비웁니다.
        /// </summary>
        public void Clear()
        {
            ThrowIfDisposed();

            lock (_syncRoot)
            {
                _tasks.Clear();
                _runtimeInfos.Clear();
                _disabledRevisitAt.Clear();
                _dueTaskQueue.Clear();
                _isDueQueueDirty = false;
            }

            TraceLog.Add("Scheduler", "All tasks cleared.");
            PublishSnapshot(force: true);
        }

        /// <summary>
        /// 스케줄러 실행 루프를 시작합니다.
        /// </summary>
        public void Start()
        {
            ThrowIfDisposed();

            CancellationTokenSource cts;

            lock (_stateSyncRoot)
            {
                if (_state != SchedulerRunState.Stopped)
                {
                    return;
                }

                cts = new CancellationTokenSource();
                _cts = cts;
                _state = SchedulerRunState.Running;
                _runTask = Task.Run(() => RunAsync(cts));
            }

            TraceLog.Add("Scheduler", "Scheduler started.");
            PublishSnapshot(force: true);
        }

        /// <summary>
        /// 스케줄러를 종료합니다. 기본은 무한 대기입니다.
        /// </summary>
        public Task StopAsync()
        {
            return StopAsync(Timeout.InfiniteTimeSpan);
        }

        /// <summary>
        /// 지정한 타임아웃 안에 스케줄러를 종료합니다.
        /// </summary>
        /// <param name="timeout">대기 시간입니다. 무한 대기는 Timeout.InfiniteTimeSpan을 사용합니다.</param>
        /// <returns>종료 완료 시 true, 타임아웃이면 false입니다.</returns>
        public async Task<bool> StopAsync(TimeSpan timeout)
        {
            ValidateTimeout(timeout);

            CancellationTokenSource? cts;
            Task? runTask;
            var rejectSelfInfiniteStop = false;

            lock (_stateSyncRoot)
            {
                if (_state == SchedulerRunState.Stopped || _state == SchedulerRunState.Disposed)
                {
                    return true;
                }

                if (timeout == Timeout.InfiniteTimeSpan && s_isSchedulerExecution.Value)
                {
                    rejectSelfInfiniteStop = true;
                }

                if (rejectSelfInfiniteStop)
                {
                    cts = null;
                    runTask = null;
                }
                else
                {
                    _state = SchedulerRunState.Stopping;
                    cts = _cts;
                    runTask = _runTask;
                }
            }

            if (rejectSelfInfiniteStop)
            {
                TraceLog.Add("Scheduler", "StopAsync with infinite timeout was rejected from scheduler execution context.");
                return false;
            }

            cts?.Cancel();
            TraceLog.Add("Scheduler", "Stop requested.");

            if (cts is null || runTask is null)
            {
                CleanupStoppedScheduler(cts);
                return true;
            }

            var stopped = await WaitForStopAsync(runTask, timeout).ConfigureAwait(false);

            if (!stopped)
            {
                AttachCleanupContinuation(runTask, cts);
                return false;
            }

            try
            {
                await runTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                CleanupStoppedScheduler(cts);
            }

            return true;
        }

        /// <summary>
        /// 동기 dispose 경로입니다.
        /// </summary>
        public void Dispose()
        {
            lock (_stateSyncRoot)
            {
                if (_state == SchedulerRunState.Disposed)
                {
                    return;
                }
            }

            try
            {
                _ = StopAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            }
            catch
            {
                // Dispose 경로에서는 종료 시도를 우선하고, 최종 정리는 아래에서 계속 진행한다.
            }

            CancellationTokenSource? cts;
            Task? runTask;

            lock (_stateSyncRoot)
            {
                if (_state == SchedulerRunState.Disposed)
                {
                    return;
                }

                _state = SchedulerRunState.Disposed;
                cts = _cts;
                runTask = _runTask;
            }

            cts?.Cancel();
            TraceLog.Add("Scheduler", "Scheduler disposed.");
            if (cts is not null && (runTask is null || runTask.IsCompleted))
            {
                CleanupStoppedScheduler(cts);
            }

            SnapshotChanged = null;
            SchedulerError = null;
        }

        /// <summary>
        /// 비동기 dispose 경로입니다.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            lock (_stateSyncRoot)
            {
                if (_state == SchedulerRunState.Disposed)
                {
                    return;
                }
            }

            await StopAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            Dispose();
        }

        private async Task RunAsync(CancellationTokenSource cts)
        {
            var cancellationToken = cts.Token;

            try
            {
                s_isSchedulerExecution.Value = true;

                while (!cancellationToken.IsCancellationRequested)
                {
                    var now = DateTimeOffset.Now;
                    CopyRunnableTasks(now, _executionBuffer);

                    foreach (var task in _executionBuffer)
                    {
                        if (!IsTaskRegistered(task))
                        {
                            continue;
                        }

                        await ExecuteTaskAsync(task, now, cancellationToken).ConfigureAwait(false);
                    }

                    PublishSnapshotIfDue(now);

                    var delay = GetLoopDelay(now);
                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                RaiseSchedulerError(ex);
            }
            finally
            {
                s_isSchedulerExecution.Value = false;
                CleanupStoppedScheduler(cts);
            }
        }

        private void CopyRunnableTasks(DateTimeOffset now, List<IScheduledTask> destination)
        {
            destination.Clear();

            lock (_syncRoot)
            {
                RebuildDueQueueIfDirtyUnsafe();

                while (_dueTaskQueue.TryPeek(out var task, out var duePriority))
                {
                    if (!_runtimeInfos.ContainsKey(task))
                    {
                        _dueTaskQueue.Dequeue();
                        _disabledRevisitAt.Remove(task);
                        continue;
                    }

                    var isEnabled = TryGetTaskValue(task, candidate => candidate.IsEnabled, false);
                    var nextRunAt = TryGetTaskValue(task, candidate => candidate.NextRunAt, DateTimeOffset.MaxValue);
                    var expectedNextRunAt = GetExpectedQueueNextRunAtUnsafe(task, isEnabled, nextRunAt);

                    if (!IsQueuePriorityCurrent(duePriority, task, expectedNextRunAt))
                    {
                        _dueTaskQueue.Dequeue();
                        EnqueueTaskUnsafe(task, expectedNextRunAt);
                        continue;
                    }

                    if (expectedNextRunAt > now)
                    {
                        break;
                    }

                    _dueTaskQueue.Dequeue();

                    if (!isEnabled)
                    {
                        var revisitAt = now + _tickInterval;
                        _disabledRevisitAt[task] = revisitAt;
                        EnqueueTaskUnsafe(task, revisitAt);
                        continue;
                    }

                    destination.Add(task);
                }
            }

            // runnable 집합에서 우선순위(높은 순) -> 실행 예정 시각(빠른 순)으로 정렬
            destination.Sort(CompareRunnableTasks);
        }

        private bool IsTaskRegistered(IScheduledTask task)
        {
            lock (_syncRoot)
            {
                return _runtimeInfos.ContainsKey(task);
            }
        }

        private int CompareRunnableTasks(IScheduledTask x, IScheduledTask y)
        {
            var xNextRunAt = TryGetTaskValue(x, task => task.NextRunAt, DateTimeOffset.MaxValue);
            var yNextRunAt = TryGetTaskValue(y, task => task.NextRunAt, DateTimeOffset.MaxValue);

            // 예정 시각 차이가 tick 간격보다 충분히 크면, 더 먼저 예정된 태스크를 우선한다.
            var dueGapTicks = (xNextRunAt - yNextRunAt).Ticks;
            if (Math.Abs(dueGapTicks) > _tickInterval.Ticks)
            {
                return xNextRunAt.CompareTo(yNextRunAt);
            }

            var xPriority = TryGetTaskValue(x, task => task.Priority, Enum_TaskPriority.Low);
            var yPriority = TryGetTaskValue(y, task => task.Priority, Enum_TaskPriority.Low);
            var priorityComparison = yPriority.CompareTo(xPriority);
            return priorityComparison != 0
                ? priorityComparison
                : xNextRunAt.CompareTo(yNextRunAt);
        }

        private async Task ExecuteTaskAsync(IScheduledTask task, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            var completedAt = now;
            var executionContext = new SchedulerContext(now, () => ShouldYieldToHigherPriorityTask(task));
            CancellationTokenSource? quantumCts = null;
            var executionToken = cancellationToken;

            if (_timeQuantum is not null)
            {
                quantumCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                quantumCts.CancelAfter(_timeQuantum.Value);
                executionToken = quantumCts.Token;
            }

            MarkTaskStarted(task, now, DateTimeOffset.Now, Environment.CurrentManagedThreadId);
            TraceLog.Add("Task", "Task execution started.", GetTaskName(task));

            try
            {
                await task.ExecuteAsync(executionContext, executionToken).ConfigureAwait(false);

                stopwatch.Stop();
                completedAt = DateTimeOffset.Now;
                
                // Periodic 태스크는 Ready 상태로, OneShot 태스크는 Suspended 상태로 전환
                var nextState = CalculateNextTaskState(task, completedAt);
                MarkTaskCompleted(task, completedAt, stopwatch.Elapsed, GetTaskPeriod(task), nextState, Environment.CurrentManagedThreadId);
                var completionKind = executionContext.IsPreempted ? "preempted" : "completed";
                TraceLog.Add("Task", $"Task execution {completionKind} in {stopwatch.Elapsed.TotalMilliseconds:N2} ms.", GetTaskName(task));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) when (quantumCts is not null && quantumCts.IsCancellationRequested)
            {
                stopwatch.Stop();
                completedAt = DateTimeOffset.Now;
                executionContext.MarkPreempted();

                var nextState = CalculateNextTaskState(task, completedAt);
                MarkTaskCompleted(task, completedAt, stopwatch.Elapsed, GetTaskPeriod(task), nextState, Environment.CurrentManagedThreadId);
                TraceLog.Add("Task", $"Task execution preempted by time quantum in {stopwatch.Elapsed.TotalMilliseconds:N2} ms.", GetTaskName(task));
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                completedAt = DateTimeOffset.Now;
                var nextState = CalculateNextTaskState(task, completedAt);
                MarkTaskFailed(task, completedAt, stopwatch.Elapsed, GetTaskPeriod(task), ex.Message, nextState, Environment.CurrentManagedThreadId);
                TraceLog.Add("Task", $"Task execution failed: {ex.Message}", GetTaskName(task));
                RaiseSchedulerError(ex);
            }
            finally
            {
                ScheduleNextRunIfRegistered(task, now, completedAt, executionContext.IsPreempted);
                quantumCts?.Dispose();
            }
        }

        private bool ShouldYieldToHigherPriorityTask(IScheduledTask runningTask)
        {
            var runningPriority = TryGetTaskValue(runningTask, task => task.Priority, Enum_TaskPriority.Low);
            var now = DateTimeOffset.Now;

            lock (_syncRoot)
            {
                if (!_runtimeInfos.ContainsKey(runningTask))
                {
                    return false;
                }

                foreach (var candidate in _tasks)
                {
                    if (ReferenceEquals(candidate, runningTask))
                    {
                        continue;
                    }

                    if (!_runtimeInfos.ContainsKey(candidate))
                    {
                        continue;
                    }

                    if (!TryGetTaskValue(candidate, task => task.IsEnabled, false))
                    {
                        continue;
                    }

                    var candidatePriority = TryGetTaskValue(candidate, task => task.Priority, Enum_TaskPriority.Low);
                    if (candidatePriority <= runningPriority)
                    {
                        continue;
                    }

                    var candidateNextRunAt = TryGetTaskValue(candidate, task => task.NextRunAt, DateTimeOffset.MaxValue);

                    // "더 높은 우선순위" + "지금 실행 가능"이면 양보 신호를 true로 준다.
                    if (candidateNextRunAt <= now)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void MarkTaskStarted(IScheduledTask task, DateTimeOffset scheduledAt, DateTimeOffset startedAt, int threadId)
        {
            lock (_syncRoot)
            {
                if (_runtimeInfos.TryGetValue(task, out var runtimeInfo))
                {
                    runtimeInfo.MarkStarted(scheduledAt, startedAt, threadId);
                }
            }
        }

        private void MarkTaskCompleted(IScheduledTask task, DateTimeOffset completedAt, TimeSpan duration, TimeSpan period, Enum_TaskState nextState, int threadId)
        {
            lock (_syncRoot)
            {
                if (_runtimeInfos.TryGetValue(task, out var runtimeInfo))
                {
                    runtimeInfo.MarkCompleted(completedAt, duration, period, nextState, threadId);
                }
            }
        }

        private void MarkTaskFailed(IScheduledTask task, DateTimeOffset completedAt, TimeSpan duration, TimeSpan period, string error, Enum_TaskState nextState, int threadId)
        {
            lock (_syncRoot)
            {
                if (_runtimeInfos.TryGetValue(task, out var runtimeInfo))
                {
                    runtimeInfo.MarkFailed(completedAt, duration, period, error, nextState, threadId);
                }
            }
        }

        private void ScheduleNextRunIfRegistered(IScheduledTask task, DateTimeOffset scheduledAt, DateTimeOffset completedAt, bool isPreempted)
        {
            lock (_syncRoot)
            {
                if (!_runtimeInfos.ContainsKey(task))
                {
                    return;
                }

                var mode = TryGetTaskValue(task, candidate => candidate.Mode, Enum_TaskExecutionMode.OneShot);
                var period = TryGetTaskValue(task, candidate => candidate.Period, TimeSpan.Zero);
                var overrunPolicy = TryGetTaskValue(task, candidate => candidate.OverrunPolicy, Enum_TaskOverrunPolicy.FixedRate);

                // OneShot 태스크는 한 번 실행 후 자동으로 비활성화
                if (mode == Enum_TaskExecutionMode.OneShot)
                {
                    task.SetEnabled(false);
                    task.NextRunAt = DateTimeOffset.MaxValue;
                    EnqueueTaskUnsafe(task, DateTimeOffset.MaxValue);
                    TraceLog.Add("Scheduler", "OneShot task completed and disabled.", GetTaskName(task));
                }
                else
                {
                    // Periodic 태스크는 정책(FixedRate/FixedDelay/SkipMissedTicks)에 따라
                    // 다음 실행 시각을 계산한다.
                    if (isPreempted)
                    {
                        task.NextRunAt = completedAt;
                        EnqueueTaskUnsafe(task, completedAt);
                        TraceLog.Add("Scheduler", "Preempted task rescheduled immediately.", GetTaskName(task));
                        return;
                    }

                    task.NextRunAt = GetNextRunAt(mode, overrunPolicy, period, scheduledAt, completedAt);
                    EnqueueTaskUnsafe(task);
                }
            }
        }

        private TimeSpan GetLoopDelay(DateTimeOffset now)
        {
            lock (_syncRoot)
            {
                RebuildDueQueueIfDirtyUnsafe();

                while (_dueTaskQueue.TryPeek(out var task, out var duePriority))
                {
                    if (!_runtimeInfos.ContainsKey(task))
                    {
                        _dueTaskQueue.Dequeue();
                        _disabledRevisitAt.Remove(task);
                        continue;
                    }

                    var isEnabled = TryGetTaskValue(task, candidate => candidate.IsEnabled, false);
                    var latestNextRunAt = TryGetTaskValue(task, candidate => candidate.NextRunAt, DateTimeOffset.MaxValue);
                    var expectedNextRunAt = GetExpectedQueueNextRunAtUnsafe(task, isEnabled, latestNextRunAt);

                    if (!IsQueuePriorityCurrent(duePriority, task, expectedNextRunAt))
                    {
                        _dueTaskQueue.Dequeue();
                        EnqueueTaskUnsafe(task, expectedNextRunAt);
                        continue;
                    }

                    if (expectedNextRunAt <= now)
                    {
                        return TimeSpan.Zero;
                    }

                    var untilDue = expectedNextRunAt - now;
                    return untilDue < _tickInterval
                        ? untilDue
                        : _tickInterval;
                }

                return _tickInterval;
            }
        }

        private void RebuildDueQueueIfDirtyUnsafe()
        {
            if (!_isDueQueueDirty)
            {
                return;
            }

            _dueTaskQueue.Clear();

            foreach (var task in _tasks)
            {
                if (_runtimeInfos.ContainsKey(task))
                {
                    EnqueueTaskUnsafe(task);
                }
            }

            _isDueQueueDirty = false;
        }

        private void EnqueueTaskUnsafe(IScheduledTask task)
        {
            var isEnabled = TryGetTaskValue(task, candidate => candidate.IsEnabled, false);
            var nextRunAt = TryGetTaskValue(task, candidate => candidate.NextRunAt, DateTimeOffset.MaxValue);
            EnqueueTaskUnsafe(task, GetExpectedQueueNextRunAtUnsafe(task, isEnabled, nextRunAt));
        }

        private void EnqueueTaskUnsafe(IScheduledTask task, DateTimeOffset nextRunAt)
        {
            _dueTaskQueue.Enqueue(task, CreateDuePriority(task, nextRunAt));
        }

        private DueTaskPriority CreateDuePriority(IScheduledTask task, DateTimeOffset nextRunAt)
        {
            return new DueTaskPriority(
                nextRunAt.UtcTicks,
                -1 * (int)TryGetTaskValue(task, candidate => candidate.Priority, Enum_TaskPriority.Low),
                Interlocked.Increment(ref _dueSequence));
        }

        private static bool IsQueuePriorityCurrent(DueTaskPriority queuedPriority, IScheduledTask task, DateTimeOffset nextRunAt)
        {
            var currentPriorityOrder = -1 * (int)TryGetTaskValue(task, candidate => candidate.Priority, Enum_TaskPriority.Low);
            return queuedPriority.NextRunTicks == nextRunAt.UtcTicks
                && queuedPriority.PriorityOrder == currentPriorityOrder;
        }

        private DateTimeOffset GetExpectedQueueNextRunAtUnsafe(IScheduledTask task, bool isEnabled, DateTimeOffset nextRunAt)
        {
            if (isEnabled)
            {
                _disabledRevisitAt.Remove(task);
                return nextRunAt;
            }

            return _disabledRevisitAt.TryGetValue(task, out var revisitAt)
                ? revisitAt
                : nextRunAt;
        }

        private void PublishSnapshotIfDue(DateTimeOffset now)
        {
            lock (_stateSyncRoot)
            {
                if (now < _nextSnapshotAt)
                {
                    return;
                }

                _nextSnapshotAt = now + _snapshotInterval;
            }

            PublishSnapshot();
        }

        private void PublishSnapshot(bool force = false)
        {
            if (force)
            {
                lock (_stateSyncRoot)
                {
                    _nextSnapshotAt = DateTimeOffset.Now + _snapshotInterval;
                }
            }

            var taskSnapshots = CreateTaskSnapshots();

            RaiseSnapshotChanged(new SchedulerSnapshot(
                DateTimeOffset.Now,
                IsRunning,
                taskSnapshots));
        }

        private IReadOnlyList<ScheduledTaskSnapshot> CreateTaskSnapshots()
        {
            List<(IScheduledTask Task, TaskRuntimeSnapshot Runtime)> snapshotSources;

            lock (_syncRoot)
            {
                snapshotSources = new List<(IScheduledTask Task, TaskRuntimeSnapshot Runtime)>(_tasks.Count);

                foreach (var task in _tasks)
                {
                    if (_runtimeInfos.TryGetValue(task, out var runtimeInfo))
                    {
                        snapshotSources.Add((task, runtimeInfo.ToSnapshot()));
                    }
                }
            }

            var snapshots = new List<ScheduledTaskSnapshot>(snapshotSources.Count);

            foreach (var source in snapshotSources)
            {
                snapshots.Add(CreateTaskSnapshot(source.Task, source.Runtime));
            }

            return snapshots;
        }

        private static ScheduledTaskSnapshot CreateTaskSnapshot(IScheduledTask task, TaskRuntimeSnapshot runtimeInfo)
        {
            var name = GetTaskName(task);
            var isEnabled = TryGetTaskValue(task, candidate => candidate.IsEnabled, false);
            var nextRunAt = TryGetTaskValue(task, candidate => candidate.NextRunAt, DateTimeOffset.MaxValue);

            return new ScheduledTaskSnapshot(
                name,
                TryGetTaskValue(task, candidate => candidate.Priority, Enum_TaskPriority.Low),
                TryGetTaskValue(task, candidate => candidate.Period, TimeSpan.Zero),
                TryGetTaskValue(task, candidate => candidate.Mode, Enum_TaskExecutionMode.OneShot),
                GetSnapshotState(isEnabled, nextRunAt, runtimeInfo),
                nextRunAt,
                isEnabled,
                GetTaskStatus(task),
                runtimeInfo.RunCount,
                runtimeInfo.LastStartedAt,
                runtimeInfo.LastCompletedAt,
                runtimeInfo.LastDuration,
                runtimeInfo.MinDuration,
                runtimeInfo.MaxDuration,
                runtimeInfo.AverageDuration,
                runtimeInfo.LastStartDelay,
                runtimeInfo.MaxStartDelay,
                runtimeInfo.DeadlineMissCount,
                runtimeInfo.LastError,
                runtimeInfo.LastStartedThreadId,
                runtimeInfo.LastCompletedThreadId);
        }

        /// <summary>
        /// 태스크 실행 후 다음 상태를 계산합니다.
        /// Periodic 태스크는 Ready 상태로, OneShot 태스크는 Suspended 상태로 전환합니다.
        /// </summary>
        private static Enum_TaskState CalculateNextTaskState(IScheduledTask task, DateTimeOffset completedAt)
        {
            var mode = TryGetTaskValue(task, candidate => candidate.Mode, Enum_TaskExecutionMode.OneShot);
            var isEnabled = TryGetTaskValue(task, candidate => candidate.IsEnabled, false);

            if (mode == Enum_TaskExecutionMode.OneShot)
            {
                return Enum_TaskState.Suspended;
            }

            // Periodic 태스크는 enabled 여부에 따라 Ready 또는 Suspended
            return isEnabled ? Enum_TaskState.Ready : Enum_TaskState.Suspended;
        }

        private static DateTimeOffset GetNextRunAt(
            Enum_TaskExecutionMode mode,
            Enum_TaskOverrunPolicy overrunPolicy,
            TimeSpan period,
            DateTimeOffset scheduledAt,
            DateTimeOffset completedAt)
        {
            if (mode != Enum_TaskExecutionMode.Periodic || period <= TimeSpan.Zero)
            {
                return DateTimeOffset.MaxValue;
            }

            try
            {
                return overrunPolicy switch
                {
                    Enum_TaskOverrunPolicy.FixedDelay => completedAt + period,
                    Enum_TaskOverrunPolicy.SkipMissedTicks => GetNextRunAfterMissedTicks(scheduledAt, completedAt, period),
                    _ => scheduledAt + period,
                };
            }
            catch (ArgumentOutOfRangeException)
            {
                return DateTimeOffset.MaxValue;
            }
            catch (OverflowException)
            {
                return DateTimeOffset.MaxValue;
            }
        }

        private static DateTimeOffset GetNextRunAfterMissedTicks(DateTimeOffset scheduledAt, DateTimeOffset completedAt, TimeSpan period)
        {
            var elapsedTicks = (completedAt - scheduledAt).Ticks;
            if (elapsedTicks < period.Ticks)
            {
                return scheduledAt + period;
            }

            // 완료 시각까지 이미 지난 주기 수를 계산해서
            // "현재 시각 이후" 첫 주기로 점프한다.
            var missedPeriods = elapsedTicks / period.Ticks + 1;
            return scheduledAt + TimeSpan.FromTicks(checked(period.Ticks * missedPeriods));
        }

        private static Enum_TaskState GetSnapshotState(bool isEnabled, DateTimeOffset nextRunAt, TaskRuntimeSnapshot runtimeInfo)
        {
            if (!isEnabled)
            {
                return Enum_TaskState.Suspended;
            }

            if (runtimeInfo.State == Enum_TaskState.Running)
            {
                return Enum_TaskState.Running;
            }

            return nextRunAt <= DateTimeOffset.Now
                ? Enum_TaskState.Ready
                : Enum_TaskState.Blocked;
        }

        private static string GetTaskName(IScheduledTask task)
        {
            return TryGetTaskValue(task, candidate => candidate.Name, task.GetType().Name);
        }

        private static string GetTaskStatus(IScheduledTask task)
        {
            try
            {
                return task.Status;
            }
            catch (Exception ex)
            {
                return $"Status error: {ex.Message}";
            }
        }

        private static TimeSpan GetTaskPeriod(IScheduledTask task)
        {
            return TryGetTaskValue(task, candidate => candidate.Period, TimeSpan.Zero);
        }

        private static TValue TryGetTaskValue<TValue>(IScheduledTask task, Func<IScheduledTask, TValue> valueFactory, TValue fallback)
        {
            try
            {
                return valueFactory(task);
            }
            catch
            {
                return fallback;
            }
        }

        private static async Task<bool> WaitForStopAsync(Task runTask, TimeSpan timeout)
        {
            if (timeout == Timeout.InfiniteTimeSpan)
            {
                await Task.WhenAny(runTask).ConfigureAwait(false);
                return true;
            }

            using var delayCts = new CancellationTokenSource();
            var delayTask = Task.Delay(timeout, delayCts.Token);
            var completedTask = await Task.WhenAny(runTask, delayTask).ConfigureAwait(false);

            if (completedTask == runTask)
            {
                delayCts.Cancel();
            }

            return completedTask == runTask;
        }

        private static void ValidateTimeout(TimeSpan timeout)
        {
            if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be non-negative or Timeout.InfiniteTimeSpan.");
            }
        }

        private void AttachCleanupContinuation(Task runTask, CancellationTokenSource cts)
        {
            _ = runTask.ContinueWith(
                _ => CleanupStoppedScheduler(cts),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private void CleanupStoppedScheduler(CancellationTokenSource? cts)
        {
            var shouldPublish = false;

            lock (_stateSyncRoot)
            {
                if (cts is not null && !ReferenceEquals(_cts, cts))
                {
                    return;
                }

                cts?.Dispose();
                _cts = null;
                _runTask = null;

                if (_state != SchedulerRunState.Disposed)
                {
                    _state = SchedulerRunState.Stopped;
                    shouldPublish = true;
                }
            }

            if (shouldPublish)
            {
                PublishSnapshot(force: true);
            }
        }

        private void RaiseSnapshotChanged(SchedulerSnapshot snapshot)
        {
            var handlers = SnapshotChanged;
            if (handlers is null)
            {
                return;
            }

            foreach (EventHandler<SchedulerSnapshot> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(this, snapshot);
                }
                catch (Exception ex)
                {
                    RaiseSchedulerError(ex);
                }
            }
        }

        private void RaiseSchedulerError(Exception exception)
        {
            var handlers = SchedulerError;
            if (handlers is null)
            {
                Debug.WriteLine(exception);
                return;
            }

            foreach (EventHandler<Exception> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(this, exception);
                }
                catch
                {
                    Debug.WriteLine(exception);
                }
            }
        }

        private void ThrowIfDisposed()
        {
            lock (_stateSyncRoot)
            {
                ObjectDisposedException.ThrowIf(_state == SchedulerRunState.Disposed, this);
            }
        }

        private enum SchedulerRunState
        {
            Stopped,
            Running,
            Stopping,
            Disposed
        }

        private readonly record struct DueTaskPriority(
            long NextRunTicks,
            int PriorityOrder,
            long Sequence);

        private sealed class DueTaskPriorityComparer : IComparer<DueTaskPriority>
        {
            public static DueTaskPriorityComparer Instance { get; } = new();

            public int Compare(DueTaskPriority x, DueTaskPriority y)
            {
                var nextRunComparison = x.NextRunTicks.CompareTo(y.NextRunTicks);
                if (nextRunComparison != 0)
                {
                    return nextRunComparison;
                }

                var priorityComparison = x.PriorityOrder.CompareTo(y.PriorityOrder);
                if (priorityComparison != 0)
                {
                    return priorityComparison;
                }

                return x.Sequence.CompareTo(y.Sequence);
            }
        }
    }
}
