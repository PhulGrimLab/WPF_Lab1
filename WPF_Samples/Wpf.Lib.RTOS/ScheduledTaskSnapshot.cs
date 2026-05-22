namespace Wpf.Lib.RTOS;

/// <summary>
/// 특정 시점의 단일 태스크 실행 상태와 통계를 담는 읽기 전용 스냅샷입니다.
/// </summary>
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
    string? LastError,
    int? LastStartedThreadId,
    int? LastCompletedThreadId);
