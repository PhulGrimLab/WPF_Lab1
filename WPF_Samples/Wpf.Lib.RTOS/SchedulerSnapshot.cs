namespace Wpf.Lib.RTOS;

public sealed record SchedulerSnapshot(
    DateTimeOffset CreatedAt,
    bool IsRunning,
    IReadOnlyList<ScheduledTaskSnapshot> Tasks);
