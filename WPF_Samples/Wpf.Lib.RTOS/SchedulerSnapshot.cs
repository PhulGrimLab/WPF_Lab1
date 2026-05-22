namespace Wpf.Lib.RTOS;

/// <summary>
/// 특정 시점의 스케줄러 전체 상태를 담는 읽기 전용 스냅샷입니다.
/// </summary>
public sealed record SchedulerSnapshot(
    DateTimeOffset CreatedAt,
    bool IsRunning,
    IReadOnlyList<ScheduledTaskSnapshot> Tasks);
