namespace Wpf.Lib.RTOS;

/// <summary>
/// Task.Delay 기반 소프트웨어 타이머입니다.
/// </summary>
public sealed class RtosSoftwareTimer : IAsyncDisposable, IDisposable
{
    private readonly Func<CancellationToken, Task> _callback;
    private readonly TimeSpan _period;
    private readonly bool _isPeriodic;
    private readonly object _syncRoot = new();
    private CancellationTokenSource? _cts;
    private Task? _runTask;

    /// <summary>
    /// 타이머를 생성합니다.
    /// </summary>
    /// <param name="period">주기(또는 one-shot 지연 시간)입니다.</param>
    /// <param name="isPeriodic">true면 반복 타이머, false면 one-shot입니다.</param>
    /// <param name="callback">주기마다 호출할 콜백입니다.</param>
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

    /// <summary>
    /// 타이머가 실행 중이면 true입니다.
    /// </summary>
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

    /// <summary>
    /// 콜백 실행 중 예외가 발생하면 전달됩니다.
    /// </summary>
    public event EventHandler<Exception>? TimerError;

    /// <summary>
    /// 타이머를 시작합니다.
    /// </summary>
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

    /// <summary>
    /// 타이머 중지를 요청하고 실행 중 루프 종료를 기다립니다.
    /// </summary>
    public async Task StopAsync()
    {
        CancellationTokenSource? cts;
        Task? runTask;

        lock (_syncRoot)
        {
            cts = _cts;
            runTask = _runTask;
            _cts = null;
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

        lock (_syncRoot)
        {
            if (ReferenceEquals(_runTask, runTask))
            {
                _runTask = null;
            }
        }

        cts?.Dispose();
    }

    /// <summary>
    /// 동기 dispose 경로입니다.
    /// </summary>
    public void Dispose()
    {
        CancellationTokenSource? cts;
        Task? runTask;

        lock (_syncRoot)
        {
            cts = _cts;
            runTask = _runTask;
            _cts = null;
        }

        cts?.Cancel();
        DisposeCancellationSourceWhenStopped(this, runTask, cts);
    }

    /// <summary>
    /// 비동기 dispose 경로입니다.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        do
        {
            await Task.Delay(_period, cancellationToken).ConfigureAwait(false);

            try
            {
                await _callback(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                RaiseTimerError(ex);

                // One-shot 타이머는 콜백 실패 시 종료하고,
                // periodic 타이머는 다음 주기에 계속 실행한다.
                if (!_isPeriodic)
                {
                    break;
                }
            }
        }
        while (_isPeriodic && !cancellationToken.IsCancellationRequested);
    }

    private void RaiseTimerError(Exception exception)
    {
        var handlers = TimerError;
        if (handlers is null)
        {
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
            }
        }
    }

    private static void DisposeCancellationSourceWhenStopped(RtosSoftwareTimer owner, Task? runTask, CancellationTokenSource? cts)
    {
        if (cts is null)
        {
            return;
        }

        if (runTask is null || runTask.IsCompleted)
        {
            owner.ClearRunTaskIfMatches(runTask);
            cts.Dispose();
            ObserveTaskException(runTask);
            return;
        }

        _ = runTask.ContinueWith(
            task =>
            {
                owner.ClearRunTaskIfMatches(task);
                ObserveTaskException(task);
                cts.Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void ClearRunTaskIfMatches(Task? expected)
    {
        lock (_syncRoot)
        {
            if (ReferenceEquals(_runTask, expected))
            {
                _runTask = null;
            }
        }
    }

    private static void ObserveTaskException(Task? task)
    {
        _ = task?.Exception;
    }
}
