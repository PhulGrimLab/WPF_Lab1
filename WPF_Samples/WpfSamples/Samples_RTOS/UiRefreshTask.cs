using Wpf.Lib.RTOS;

namespace WpfSamples.Samples_RTOS;

internal sealed class UiRefreshTask : IScheduledTask
{
    private bool _isEnabled = true;

    public string Name => "UI Refresh Task";
    public Enum_TaskPriority Priority => Enum_TaskPriority.High;
    public TimeSpan Period => TimeSpan.FromMilliseconds(250);
    public Enum_TaskExecutionMode Mode => Enum_TaskExecutionMode.Periodic;
    public Enum_TaskOverrunPolicy OverrunPolicy => Enum_TaskOverrunPolicy.FixedRate;
    public DateTimeOffset NextRunAt { get; set; } = DateTimeOffset.Now;
    public bool IsEnabled => _isEnabled;
    public string Status => $"Refresh tick = {RefreshTick:N0}";

    public int RefreshTick { get; private set; }

    public Task ExecuteAsync(SchedulerContext context, CancellationToken cancellationToken)
    {
        // UI 갱신 틱 증가
        // 주기: 250ms (고우선순위로 스케줄러에서 우선 실행됨)
        RefreshTick++;

        // 참고: 무거운 작업이 필요한 경우 별도 스레드에서 실행하거나
        // Task.Delay를 사용하되, 스케줄러의 다음 태스크 실행을 블로킹하지 않도록 주의
        return Task.CompletedTask;
    }

    public void SetEnabled(bool isEnabled)
    {
        _isEnabled = isEnabled;
    }
}
