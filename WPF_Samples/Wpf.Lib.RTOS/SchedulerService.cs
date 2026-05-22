using System.Diagnostics;

namespace Wpf.Lib.RTOS
{
    public sealed class SchedulerService : IDisposable, IAsyncDisposable
    {
        private readonly object _syncRoot = new();
        private readonly object _stateSyncRoot = new();
        private readonly List<IScheduledTask> _tasks = [];
        private readonly List<IScheduledTask> _runnableBuffer = [];
        private readonly List<IScheduledTask> _executionBuffer = [];
        private readonly Dictionary<IScheduledTask, TaskRuntimeInfo> _runtimeInfos = [];
        private static readonly AsyncLocal<bool> s_isSchedulerExecution = new();
        private readonly TimeSpan _tickInterval;
        private readonly TimeSpan _snapshotInterval;
        private CancellationTokenSource? _cts;
        private Task? _runTask;
        private DateTimeOffset _nextSnapshotAt = DateTimeOffset.MinValue;
        private SchedulerRunState _state = SchedulerRunState.Stopped;

        public SchedulerService(TimeSpan? tickInterval = null, TimeSpan? snapshotInterval = null)
        {
            _tickInterval = tickInterval ?? TimeSpan.FromMilliseconds(10);
            _snapshotInterval = snapshotInterval ?? TimeSpan.FromMilliseconds(100);

            if (_tickInterval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(tickInterval), "Tick interval must be greater than zero.");
            }

            if (_snapshotInterval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(snapshotInterval), "Snapshot interval must be greater than zero.");
            }
        }

        public event EventHandler<SchedulerSnapshot>? SnapshotChanged;
        public event EventHandler<Exception>? SchedulerError;
        public RtosTraceLog TraceLog { get; } = new();

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
            }

            TraceLog.Add("Scheduler", "Task registered.", GetTaskName(task));
            PublishSnapshot(force: true);
        }

        public bool Unregister(IScheduledTask task)
        {
            ThrowIfDisposed();

            ArgumentNullException.ThrowIfNull(task);

            var removed = false;

            lock (_syncRoot)
            {
                removed = _tasks.Remove(task);
                _runtimeInfos.Remove(task);
            }

            if (removed)
            {
                TraceLog.Add("Scheduler", "Task unregistered.", GetTaskName(task));
                PublishSnapshot(force: true);
            }

            return removed;
        }

        public void Clear()
        {
            ThrowIfDisposed();

            lock (_syncRoot)
            {
                _tasks.Clear();
                _runtimeInfos.Clear();
            }

            TraceLog.Add("Scheduler", "All tasks cleared.");
            PublishSnapshot(force: true);
        }

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

        public Task StopAsync()
        {
            return StopAsync(Timeout.InfiniteTimeSpan);
        }

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
                    await Task.Delay(_tickInterval, cancellationToken).ConfigureAwait(false);
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
            lock (_syncRoot)
            {
                _runnableBuffer.Clear();

                foreach (var task in _tasks)
                {
                    _runnableBuffer.Add(task);
                }
            }

            destination.Clear();

            foreach (var task in _runnableBuffer)
            {
                if (TryGetTaskValue(task, candidate => candidate.IsEnabled, false)
                    && TryGetTaskValue(task, candidate => candidate.NextRunAt, DateTimeOffset.MaxValue) <= now)
                {
                    destination.Add(task);
                }
            }

            destination.Sort(CompareRunnableTasks);
        }

        private bool IsTaskRegistered(IScheduledTask task)
        {
            lock (_syncRoot)
            {
                return _runtimeInfos.ContainsKey(task);
            }
        }

        private static int CompareRunnableTasks(IScheduledTask x, IScheduledTask y)
        {
            var xPriority = TryGetTaskValue(x, task => task.Priority, Enum_TaskPriority.Low);
            var yPriority = TryGetTaskValue(y, task => task.Priority, Enum_TaskPriority.Low);
            var priorityComparison = yPriority.CompareTo(xPriority);
            return priorityComparison != 0
                ? priorityComparison
                : TryGetTaskValue(x, task => task.NextRunAt, DateTimeOffset.MaxValue)
                    .CompareTo(TryGetTaskValue(y, task => task.NextRunAt, DateTimeOffset.MaxValue));
        }

        private async Task ExecuteTaskAsync(IScheduledTask task, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            var completedAt = now;
            var executionContext = new SchedulerContext(now, () => ShouldYieldToHigherPriorityTask(task));

            MarkTaskStarted(task, now, DateTimeOffset.Now);
            TraceLog.Add("Task", "Task execution started.", GetTaskName(task));

            try
            {
                await task.ExecuteAsync(executionContext, cancellationToken).ConfigureAwait(false);

                stopwatch.Stop();
                completedAt = DateTimeOffset.Now;
                
                // Periodic 태스크는 Ready 상태로, OneShot 태스크는 Suspended 상태로 전환
                var nextState = CalculateNextTaskState(task, completedAt);
                MarkTaskCompleted(task, completedAt, stopwatch.Elapsed, GetTaskPeriod(task), nextState);
                TraceLog.Add("Task", $"Task execution completed in {stopwatch.Elapsed.TotalMilliseconds:N2} ms.", GetTaskName(task));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                completedAt = DateTimeOffset.Now;
                var nextState = CalculateNextTaskState(task, completedAt);
                MarkTaskFailed(task, completedAt, stopwatch.Elapsed, GetTaskPeriod(task), ex.Message, nextState);
                TraceLog.Add("Task", $"Task execution failed: {ex.Message}", GetTaskName(task));
                RaiseSchedulerError(ex);
            }
            finally
            {
                ScheduleNextRunIfRegistered(task, now, completedAt);
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
                    if (candidateNextRunAt <= now)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        private void MarkTaskStarted(IScheduledTask task, DateTimeOffset scheduledAt, DateTimeOffset startedAt)
        {
            lock (_syncRoot)
            {
                if (_runtimeInfos.TryGetValue(task, out var runtimeInfo))
                {
                    runtimeInfo.MarkStarted(scheduledAt, startedAt);
                }
            }
        }

        private void MarkTaskCompleted(IScheduledTask task, DateTimeOffset completedAt, TimeSpan duration, TimeSpan period, Enum_TaskState nextState)
        {
            lock (_syncRoot)
            {
                if (_runtimeInfos.TryGetValue(task, out var runtimeInfo))
                {
                    runtimeInfo.MarkCompleted(completedAt, duration, period, nextState);
                }
            }
        }

        private void MarkTaskFailed(IScheduledTask task, DateTimeOffset completedAt, TimeSpan duration, TimeSpan period, string error, Enum_TaskState nextState)
        {
            lock (_syncRoot)
            {
                if (_runtimeInfos.TryGetValue(task, out var runtimeInfo))
                {
                    runtimeInfo.MarkFailed(completedAt, duration, period, error, nextState);
                }
            }
        }

        private void ScheduleNextRunIfRegistered(IScheduledTask task, DateTimeOffset scheduledAt, DateTimeOffset completedAt)
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
                    TraceLog.Add("Scheduler", "OneShot task completed and disabled.", GetTaskName(task));
                }
                else
                {
                    task.NextRunAt = GetNextRunAt(mode, overrunPolicy, period, scheduledAt, completedAt);
                }
            }
        }

        private void PublishSnapshotIfDue(DateTimeOffset now)
        {
            if (now < _nextSnapshotAt)
            {
                return;
            }

            _nextSnapshotAt = now + _snapshotInterval;
            PublishSnapshot();
        }

        private void PublishSnapshot(bool force = false)
        {
            if (force)
            {
                _nextSnapshotAt = DateTimeOffset.Now + _snapshotInterval;
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
                runtimeInfo.LastError);
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
    }
}
