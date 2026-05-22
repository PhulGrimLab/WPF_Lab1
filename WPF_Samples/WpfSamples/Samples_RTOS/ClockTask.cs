using Wpf.Lib.RTOS;

namespace WpfSamples.Samples_RTOS;

internal sealed class ClockTask : IScheduledTask
{
    private bool _isEnabled = true;

    public string Name => "Clock Task";
    public Enum_TaskPriority Priority => Enum_TaskPriority.Low;
    public TimeSpan Period => TimeSpan.FromSeconds(1);
    public Enum_TaskExecutionMode Mode => Enum_TaskExecutionMode.Periodic;
    public Enum_TaskOverrunPolicy OverrunPolicy => Enum_TaskOverrunPolicy.FixedRate;
    public DateTimeOffset NextRunAt { get; set; } = DateTimeOffset.Now;
    public bool IsEnabled => _isEnabled;
    public string Status => CurrentTimeText;

    public string CurrentTimeText { get; private set; } = DateTime.Now.ToString("HH:mm:ss");

    public Task ExecuteAsync(SchedulerContext context, CancellationToken cancellationToken)
    {
        CurrentTimeText = context.Now.LocalDateTime.ToString("HH:mm:ss");
        return Task.CompletedTask;
    }

    public void SetEnabled(bool isEnabled)
    {
        _isEnabled = isEnabled;
    }
}
