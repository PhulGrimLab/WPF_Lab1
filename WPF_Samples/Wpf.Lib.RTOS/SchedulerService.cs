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
        }

        public event EventHandler<SchedulerSnapshot>? SnapshotChanged;
        public event EventHandler<Exception>? SchedulerError;

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

            PublishSnapshot(force: true);
        }

        public Task StopAsync()
        {
            return StopAsync(Timeout.InfiniteTimeSpan);
        }

        public async Task<bool> StopAsync(TimeSpan timeout)
        {
            CancellationTokenSource? cts;
            Task? runTask;

            lock (_stateSyncRoot)
            {
                if (_state == SchedulerRunState.Stopped || _state == SchedulerRunState.Disposed)
                {
                    return true;
                }

                _state = SchedulerRunState.Stopping;
                cts = _cts;
                runTask = _runTask;
            }

            cts?.Cancel();

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
            CancellationTokenSource? cts;

            lock (_stateSyncRoot)
            {
                if (_state == SchedulerRunState.Disposed)
                {
                    return;
                }

                _state = SchedulerRunState.Disposed;
                cts = _cts;
                _cts = null;
                _runTask = null;
            }

            cts?.Cancel();
            cts?.Dispose();
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
                if (task.IsEnabled && task.NextRunAt <= now)
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
            var priorityComparison = y.Priority.CompareTo(x.Priority);
            return priorityComparison != 0
                ? priorityComparison
                : x.NextRunAt.CompareTo(y.NextRunAt);
        }

        private async Task ExecuteTaskAsync(IScheduledTask task, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();

            MarkTaskStarted(task, now);

            try
            {
                await task.ExecuteAsync(new SchedulerContext(now), cancellationToken).ConfigureAwait(false);

                stopwatch.Stop();
                MarkTaskCompleted(task, DateTimeOffset.Now, stopwatch.Elapsed);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                MarkTaskFailed(task, DateTimeOffset.Now, stopwatch.Elapsed, ex.Message);
                RaiseSchedulerError(ex);
            }
            finally
            {
                ScheduleNextRunIfRegistered(task, now);
            }
        }

        private void MarkTaskStarted(IScheduledTask task, DateTimeOffset now)
        {
            lock (_syncRoot)
            {
                if (_runtimeInfos.TryGetValue(task, out var runtimeInfo))
                {
                    runtimeInfo.MarkStarted(now);
                }
            }
        }

        private void MarkTaskCompleted(IScheduledTask task, DateTimeOffset completedAt, TimeSpan duration)
        {
            lock (_syncRoot)
            {
                if (_runtimeInfos.TryGetValue(task, out var runtimeInfo))
                {
                    runtimeInfo.MarkCompleted(completedAt, duration);
                }
            }
        }

        private void MarkTaskFailed(IScheduledTask task, DateTimeOffset completedAt, TimeSpan duration, string error)
        {
            lock (_syncRoot)
            {
                if (_runtimeInfos.TryGetValue(task, out var runtimeInfo))
                {
                    runtimeInfo.MarkFailed(completedAt, duration, error);
                }
            }
        }

        private void ScheduleNextRunIfRegistered(IScheduledTask task, DateTimeOffset now)
        {
            lock (_syncRoot)
            {
                if (!_runtimeInfos.ContainsKey(task))
                {
                    return;
                }

                task.NextRunAt = task.Mode == Enum_TaskExecutionMode.Periodic
                    ? now + task.Period
                    : DateTimeOffset.MaxValue;
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
            return new ScheduledTaskSnapshot(
                task.Name,
                task.Priority,
                task.Period,
                task.Mode,
                task.NextRunAt,
                task.IsEnabled,
                task.Status,
                runtimeInfo.RunCount,
                runtimeInfo.LastStartedAt,
                runtimeInfo.LastCompletedAt,
                runtimeInfo.LastDuration,
                runtimeInfo.LastError);
        }

        private static async Task<bool> WaitForStopAsync(Task runTask, TimeSpan timeout)
        {
            if (timeout == Timeout.InfiniteTimeSpan)
            {
                await Task.WhenAny(runTask).ConfigureAwait(false);
                return true;
            }

            if (timeout < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be non-negative or Timeout.InfiniteTimeSpan.");
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
