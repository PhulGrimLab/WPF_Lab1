namespace Wpf.Lib.RTOS;

public sealed class RtosSoftwareTimer : IAsyncDisposable, IDisposable
{
    private readonly Func<CancellationToken, Task> _callback;
    private readonly TimeSpan _period;
    private readonly bool _isPeriodic;
    private readonly object _syncRoot = new();
    private CancellationTokenSource? _cts;
    private Task? _runTask;

    public RtosSoftwareTimer(TimeSpan period, bool isPeriodic, Func<CancellationToken, Task> callback)
    {
        if (period <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(period), "Timer period must be greater than zero.");
        }

        _period = period;
        _isPeriodic = isPeriodic;
        _callback = callback ?? throw new ArgumentNullException(nameof(callback));
    }

    public bool IsRunning
    {
        get
        {
            lock (_syncRoot)
            {
                return _runTask is not null && !_runTask.IsCompleted;
            }
        }
    }

    public void Start()
    {
        lock (_syncRoot)
        {
            if (_runTask is not null && !_runTask.IsCompleted)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            _runTask = Task.Run(() => RunAsync(_cts.Token));
        }
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cts;
        Task? runTask;

        lock (_syncRoot)
        {
            cts = _cts;
            runTask = _runTask;
            _cts = null;
            _runTask = null;
        }

        cts?.Cancel();

        if (runTask is not null)
        {
            try
            {
                await runTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        cts?.Dispose();
    }

    public void Dispose()
    {
        CancellationTokenSource? cts;
        Task? runTask;

        lock (_syncRoot)
        {
            cts = _cts;
            runTask = _runTask;
            _cts = null;
            _runTask = null;
        }

        cts?.Cancel();
        DisposeCancellationSourceWhenStopped(runTask, cts);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        do
        {
            await Task.Delay(_period, cancellationToken).ConfigureAwait(false);
            await _callback(cancellationToken).ConfigureAwait(false);
        }
        while (_isPeriodic && !cancellationToken.IsCancellationRequested);
    }

    private static void DisposeCancellationSourceWhenStopped(Task? runTask, CancellationTokenSource? cts)
    {
        if (cts is null)
        {
            return;
        }

        if (runTask is null || runTask.IsCompleted)
        {
            cts.Dispose();
            ObserveTaskException(runTask);
            return;
        }

        _ = runTask.ContinueWith(
            task =>
            {
                ObserveTaskException(task);
                cts.Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static void ObserveTaskException(Task? task)
    {
        _ = task?.Exception;
    }
}
