namespace Wpf.Lib.RTOS;

public sealed record ScheduledTaskSnapshot(
    string Name,
    Enum_TaskPriority Priority,
    TimeSpan Period,
    Enum_TaskExecutionMode Mode,
    DateTimeOffset NextRunAt,
    bool IsEnabled,
    string Status,
    long RunCount,
    DateTimeOffset? LastStartedAt,
    DateTimeOffset? LastCompletedAt,
    TimeSpan? LastDuration,
    string? LastError);
