namespace Wpf.Lib.RTOS;

internal sealed class TaskRuntimeInfo
{
    public long RunCount { get; private set; }
    public DateTimeOffset? LastStartedAt { get; private set; }
    public DateTimeOffset? LastCompletedAt { get; private set; }
    public TimeSpan? LastDuration { get; private set; }
    public string? LastError { get; private set; }

    public void MarkStarted(DateTimeOffset startedAt)
    {
        LastStartedAt = startedAt;
    }

    public void MarkCompleted(DateTimeOffset completedAt, TimeSpan duration)
    {
        RunCount++;
        LastCompletedAt = completedAt;
        LastDuration = duration;
        LastError = null;
    }

    public void MarkFailed(DateTimeOffset completedAt, TimeSpan duration, string error)
    {
        LastCompletedAt = completedAt;
        LastDuration = duration;
        LastError = error;
    }

    public TaskRuntimeSnapshot ToSnapshot()
    {
        return new TaskRuntimeSnapshot(
            RunCount,
            LastStartedAt,
            LastCompletedAt,
            LastDuration,
            LastError);
    }
}

internal sealed record TaskRuntimeSnapshot(
    long RunCount,
    DateTimeOffset? LastStartedAt,
    DateTimeOffset? LastCompletedAt,
    TimeSpan? LastDuration,
    string? LastError);
