namespace Wpf.Lib.RTOS;

internal sealed class TaskRuntimeInfo
{
    public long RunCount { get; private set; }
    public DateTimeOffset? LastStartedAt { get; private set; }
    public DateTimeOffset? LastCompletedAt { get; private set; }
    public TimeSpan? LastDuration { get; private set; }
    public TimeSpan? MinDuration { get; private set; }
    public TimeSpan? MaxDuration { get; private set; }
    public TimeSpan? AverageDuration => RunCount == 0 ? null : TimeSpan.FromTicks(_totalDurationTicks / RunCount);
    public TimeSpan? LastStartDelay { get; private set; }
    public TimeSpan? MaxStartDelay { get; private set; }
    public long DeadlineMissCount { get; private set; }
    public string? LastError { get; private set; }
    public Enum_TaskState State { get; private set; } = Enum_TaskState.Ready;
    private long _totalDurationTicks;

    public void MarkStarted(DateTimeOffset scheduledAt, DateTimeOffset startedAt)
    {
        LastStartedAt = startedAt;
        LastStartDelay = startedAt - scheduledAt;
        MaxStartDelay = Max(MaxStartDelay, LastStartDelay.Value);
        State = Enum_TaskState.Running;
    }

    public void MarkCompleted(DateTimeOffset completedAt, TimeSpan duration, TimeSpan period, Enum_TaskState nextState)
    {
        if (RunCount < long.MaxValue)
        {
            RunCount++;
        }

        LastCompletedAt = completedAt;
        SetDuration(duration, period);
        LastError = null;
        State = nextState;
    }

    public void MarkFailed(DateTimeOffset completedAt, TimeSpan duration, TimeSpan period, string error, Enum_TaskState nextState)
    {
        LastCompletedAt = completedAt;
        SetDuration(duration, period);
        LastError = error;
        State = nextState;
    }

    public TaskRuntimeSnapshot ToSnapshot()
    {
        return new TaskRuntimeSnapshot(
            RunCount,
            LastStartedAt,
            LastCompletedAt,
            LastDuration,
            MinDuration,
            MaxDuration,
            AverageDuration,
            LastStartDelay,
            MaxStartDelay,
            DeadlineMissCount,
            LastError,
            State);
    }

    private void SetDuration(TimeSpan duration, TimeSpan period)
    {
        LastDuration = duration;
        MinDuration = Min(MinDuration, duration);
        MaxDuration = Max(MaxDuration, duration);
        _totalDurationTicks = _totalDurationTicks > long.MaxValue - duration.Ticks
            ? long.MaxValue
            : _totalDurationTicks + duration.Ticks;

        if (period > TimeSpan.Zero && duration > period)
        {
            DeadlineMissCount++;
        }
    }

    private static TimeSpan Min(TimeSpan? current, TimeSpan value)
    {
        return current is null || value < current.Value ? value : current.Value;
    }

    private static TimeSpan Max(TimeSpan? current, TimeSpan value)
    {
        return current is null || value > current.Value ? value : current.Value;
    }
}

internal sealed record TaskRuntimeSnapshot(
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
    Enum_TaskState State);
