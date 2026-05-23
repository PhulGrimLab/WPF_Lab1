using System.Diagnostics;

namespace Wpf.Lib.RTOS;

/// <summary>
/// 기본 RTOS Monitor 샘플을 라이브러리 내부에서 구성/실행하고,
/// WPF는 snapshot 구독만 하도록 분리하기 위한 데모 래퍼입니다.
/// </summary>
public sealed class SchedulerMonitorDemo : IDisposable, IAsyncDisposable
{
    private readonly object _stateSyncRoot = new();
    private readonly SchedulerService _scheduler;
    private bool _isDisposed;

    public SchedulerMonitorDemo(TimeSpan? tickInterval = null, TimeSpan? snapshotInterval = null)
    {
        _scheduler = new SchedulerService(tickInterval, snapshotInterval);
        _scheduler.SnapshotChanged += OnSchedulerSnapshotChanged;

        // 기본 Monitor 탭에 표시할 샘플 task 구성은 라이브러리에서 담당한다.
        _scheduler.Register(new CounterTask());
        _scheduler.Register(new ClockTask());
        _scheduler.Register(new UiRefreshTask());
    }

    public event EventHandler<SchedulerSnapshot>? SnapshotChanged;

    public bool IsRunning => _scheduler.IsRunning;

    public SchedulerSnapshot CurrentSnapshot => _scheduler.GetSnapshot();

    public void Start()
    {
        ThrowIfDisposed();
        _scheduler.Start();
    }

    public Task<bool> StopAsync(TimeSpan? timeout = null)
    {
        ThrowIfDisposed();
        return timeout is null
            ? _scheduler.StopAsync(Timeout.InfiniteTimeSpan)
            : _scheduler.StopAsync(timeout.Value);
    }

    public void Dispose()
    {
        lock (_stateSyncRoot)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
        }

        _scheduler.SnapshotChanged -= OnSchedulerSnapshotChanged;
        _scheduler.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        lock (_stateSyncRoot)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
        }

        _scheduler.SnapshotChanged -= OnSchedulerSnapshotChanged;
        await _scheduler.DisposeAsync().ConfigureAwait(false);
    }

    private void OnSchedulerSnapshotChanged(object? sender, SchedulerSnapshot snapshot)
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
                Debug.WriteLine(ex);
            }
        }
    }

    private void ThrowIfDisposed()
    {
        lock (_stateSyncRoot)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
        }
    }
}
