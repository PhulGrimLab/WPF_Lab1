using Wpf.Lib.RTOS;

namespace WpfSamples.Samples_RTOS;

internal sealed class UiRefreshTask : IScheduledTask
{
    public string Name => "UI Refresh Task";
    public Enum_TaskPriority Priority => Enum_TaskPriority.High;
    public TimeSpan Period => TimeSpan.FromMilliseconds(250);
    public Enum_TaskExecutionMode Mode => Enum_TaskExecutionMode.Periodic;
    public Enum_TaskOverrunPolicy OverrunPolicy => Enum_TaskOverrunPolicy.FixedRate;
    public DateTimeOffset NextRunAt { get; set; } = DateTimeOffset.Now;
    public bool IsEnabled { get; private set; } = true;
    public string Status => $"Refresh tick = {RefreshTick:N0}";

    public int RefreshTick { get; private set; }

    public async Task ExecuteAsync(SchedulerContext context, CancellationToken cancellationToken)
    {
        RefreshTick++;

        // 화면 갱신보다 조금 무거운 백그라운드 작업을 흉내낸다.
        await Task.Delay(15, cancellationToken).ConfigureAwait(false);
    }
}
