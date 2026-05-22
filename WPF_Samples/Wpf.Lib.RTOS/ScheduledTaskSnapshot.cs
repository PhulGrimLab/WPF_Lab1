namespace Wpf.Lib.RTOS;

public sealed record ScheduledTaskSnapshot(
    string Name,
    Enum_TaskPriority Priority,
    TimeSpan Period,
    Enum_TaskExecutionMode Mode,
    Enum_TaskState State,
    DateTimeOffset NextRunAt,
    bool IsEnabled,
    string Status,
    long RunCount,
    DateTimeOffset? LastStartedAt,
    DateTimeOffset? LastCompletedAt,
    TimeSpan? LastDuration,
    TimeSpan? MinDuration,
    TimeSpan? MaxDuration,
    TimeSpan? AverageDuration,
    TimeSpan? LastStartDelay,
    TimeSpan? MaxStartDelay,
    long DeadlineMissCount,
    string? LastError);
