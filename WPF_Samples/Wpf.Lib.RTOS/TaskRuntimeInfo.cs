namespace Wpf.Lib.RTOS;

internal sealed class TaskRuntimeInfo
{
    public long RunCount { get; set; }
    public DateTimeOffset? LastStartedAt { get; set; }
    public DateTimeOffset? LastCompletedAt { get; set; }
    public TimeSpan? LastDuration { get; set; }
    public string? LastError { get; set; }
}
