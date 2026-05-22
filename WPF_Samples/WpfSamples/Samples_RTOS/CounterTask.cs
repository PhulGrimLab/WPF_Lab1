using Wpf.Lib.RTOS;

namespace WpfSamples.Samples_RTOS;

internal sealed class CounterTask : IScheduledTask
{
    private bool _isEnabled = true;

    public string Name => "Counter Task";
    public Enum_TaskPriority Priority => Enum_TaskPriority.Normal;
    public TimeSpan Period => TimeSpan.FromMilliseconds(100);
    public Enum_TaskExecutionMode Mode => Enum_TaskExecutionMode.Periodic;
    public Enum_TaskOverrunPolicy OverrunPolicy => Enum_TaskOverrunPolicy.FixedRate;
    public DateTimeOffset NextRunAt { get; set; } = DateTimeOffset.Now;
    public bool IsEnabled => _isEnabled;
    public string Status => $"Count = {Count:N0}";

    public int Count { get; private set; }

    public Task ExecuteAsync(SchedulerContext context, CancellationToken cancellationToken)
    {
        Count++;
        return Task.CompletedTask;
    }

    public void SetEnabled(bool isEnabled)
    {
        _isEnabled = isEnabled;
    }
}
