using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Wpf.Lib.RTOS
{
    public sealed class SchedulerService : IDisposable
    {
        private readonly object _syncRoot = new();
        private readonly List<IScheduledTask> _tasks = [];
        private readonly Dictionary<IScheduledTask, TaskRuntimeInfo> _runtimeInfos = [];
        private readonly TimeSpan _tickInterval;
        private CancellationTokenSource? _cts;
        private Task? _runTask;

        public SchedulerService(TimeSpan? tickInterval = null)
        {
            _tickInterval = tickInterval ?? TimeSpan.FromMilliseconds(10);
        }

        public event EventHandler<SchedulerSnapshot>? SnapshotChanged;

        public bool IsRunning => _runTask is { IsCompleted: false };

        public void Register(IScheduledTask task)
        {
            lock (_syncRoot)
            {
                _tasks.Add(task);
                _runtimeInfos[task] = new TaskRuntimeInfo();
            }

            PublishSnapshot();
        }

        public void Start()
        {
            if (IsRunning)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            _runTask = Task.Run(() => RunAsync(_cts.Token));
            PublishSnapshot();
        }

        public async Task StopAsync()
        {
            if (_cts is null)
            {
                return;
            }

            _cts.Cancel();

            try
            {
                if (_runTask is not null)
                {
                    await _runTask.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                _runTask = null;
                PublishSnapshot();
            }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var now = DateTimeOffset.Now;
                var runnableTasks = GetRunnableTasks(now);

                foreach (var task in runnableTasks)
                {
                    await ExecuteTaskAsync(task, now, cancellationToken).ConfigureAwait(false);
                }

                PublishSnapshot();
                await Task.Delay(_tickInterval, cancellationToken).ConfigureAwait(false);
            }
        }

        private IReadOnlyList<IScheduledTask> GetRunnableTasks(DateTimeOffset now)
        {
            lock (_syncRoot)
            {
                return _tasks
                    .Where(task => task.IsEnabled && task.NextRunAt <= now)
                    .OrderByDescending(task => task.Priority)
                    .ThenBy(task => task.NextRunAt)
                    .ToList();
            }
        }

        private async Task ExecuteTaskAsync(IScheduledTask task, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            var runtimeInfo = GetRuntimeInfo(task);

            runtimeInfo.LastStartedAt = now;

            try
            {
                await task.ExecuteAsync(new SchedulerContext(now), cancellationToken).ConfigureAwait(false);

                runtimeInfo.RunCount++;
                runtimeInfo.LastCompletedAt = DateTimeOffset.Now;
                runtimeInfo.LastError = null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                runtimeInfo.LastCompletedAt = DateTimeOffset.Now;
                runtimeInfo.LastError = ex.Message;
            }
            finally
            {
                stopwatch.Stop();
                runtimeInfo.LastDuration = stopwatch.Elapsed;
                ScheduleNextRun(task, now);
            }
        }

        private TaskRuntimeInfo GetRuntimeInfo(IScheduledTask task)
        {
            lock (_syncRoot)
            {
                return _runtimeInfos[task];
            }
        }

        private static void ScheduleNextRun(IScheduledTask task, DateTimeOffset now)
        {
            task.NextRunAt = task.Mode == Enum_TaskExecutionMode.Periodic
                ? now + task.Period
                : DateTimeOffset.MaxValue;
        }

        private void PublishSnapshot()
        {
            SchedulerSnapshot snapshot;

            lock (_syncRoot)
            {
                snapshot = new SchedulerSnapshot(
                    DateTimeOffset.Now,
                    IsRunning,
                    _tasks.Select(CreateTaskSnapshot).ToList());
            }

            SnapshotChanged?.Invoke(this, snapshot);
        }

        private ScheduledTaskSnapshot CreateTaskSnapshot(IScheduledTask task)
        {
            var runtimeInfo = _runtimeInfos[task];

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
    }
}
