using Wpf.Lib.RTOS;

namespace WpfSamples.Samples_RTOS;

internal sealed class CounterTask : IScheduledTask
{
    public string Name => "Counter Task";
    public Enum_TaskPriority Priority => Enum_TaskPriority.Normal;
    public TimeSpan Period => TimeSpan.FromMilliseconds(100);
    public Enum_TaskExecutionMode Mode => Enum_TaskExecutionMode.Periodic;
    public DateTimeOffset NextRunAt { get; set; } = DateTimeOffset.Now;
    public bool IsEnabled { get; private set; } = true;
    public string Status => $"Count = {Count:N0}";

    public int Count { get; private set; }

    public Task ExecuteAsync(SchedulerContext context, CancellationToken cancellationToken)
    {
        Count++;
        return Task.CompletedTask;
    }
}
