namespace Wpf.Lib.RTOS;

public sealed class CounterTask : SchedulerTaskBase
{
    public CounterTask()
        : base(
            "Counter Task",
            Enum_TaskPriority.Normal,
            TimeSpan.FromMilliseconds(100),
            Enum_TaskExecutionMode.Periodic,
            Enum_TaskOverrunPolicy.FixedRate)
    {
    }

    public int Count { get; private set; }

    public override string Status => $"Count = {Count:N0}";

    public override Task ExecuteAsync(SchedulerContext context, CancellationToken cancellationToken)
    {
        Count++;
        return Task.CompletedTask;
    }
}

public sealed class ClockTask : SchedulerTaskBase
{
    public ClockTask()
        : base(
            "Clock Task",
            Enum_TaskPriority.Low,
            TimeSpan.FromSeconds(1),
            Enum_TaskExecutionMode.Periodic,
            Enum_TaskOverrunPolicy.FixedRate)
    {
    }

    public string CurrentTimeText { get; private set; } = DateTime.Now.ToString("HH:mm:ss");

    public override string Status => CurrentTimeText;

    public override Task ExecuteAsync(SchedulerContext context, CancellationToken cancellationToken)
    {
        CurrentTimeText = context.Now.LocalDateTime.ToString("HH:mm:ss");
        return Task.CompletedTask;
    }
}

public sealed class UiRefreshTask : SchedulerTaskBase
{
    public UiRefreshTask()
        : base(
            "UI Refresh Task",
            Enum_TaskPriority.High,
            TimeSpan.FromMilliseconds(250),
            Enum_TaskExecutionMode.Periodic,
            Enum_TaskOverrunPolicy.FixedRate)
    {
    }

    public int RefreshTick { get; private set; }

    public override string Status => $"Refresh tick = {RefreshTick:N0}";

    public override Task ExecuteAsync(SchedulerContext context, CancellationToken cancellationToken)
    {
        RefreshTick++;
        return Task.CompletedTask;
    }
}
