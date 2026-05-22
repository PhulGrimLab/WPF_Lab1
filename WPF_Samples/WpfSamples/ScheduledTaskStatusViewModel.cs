using Wpf.Lib.RTOS;

namespace WpfSamples;

internal sealed class ScheduledTaskStatusViewModel
{
    public ScheduledTaskStatusViewModel(ScheduledTaskSnapshot snapshot)
    {
        Name = snapshot.Name;
        Priority = snapshot.Priority.ToString();
        Period = $"{snapshot.Period.TotalMilliseconds:N0} ms";
        Mode = snapshot.Mode.ToString();
        IsEnabled = snapshot.IsEnabled;
        Status = snapshot.Status;
        RunCount = snapshot.RunCount;
        LastStartedAt = snapshot.LastStartedAt?.LocalDateTime.ToString("HH:mm:ss.fff") ?? "-";
        LastCompletedAt = snapshot.LastCompletedAt?.LocalDateTime.ToString("HH:mm:ss.fff") ?? "-";
        LastDuration = snapshot.LastDuration is null
            ? "-"
            : $"{snapshot.LastDuration.Value.TotalMilliseconds:N2} ms";
        NextRunAt = snapshot.NextRunAt == DateTimeOffset.MaxValue
            ? "-"
            : snapshot.NextRunAt.LocalDateTime.ToString("HH:mm:ss.fff");
        LastError = string.IsNullOrWhiteSpace(snapshot.LastError) ? "-" : snapshot.LastError;
    }

    public string Name { get; }
    public string Priority { get; }
    public string Period { get; }
    public string Mode { get; }
    public bool IsEnabled { get; }
    public string Status { get; }
    public long RunCount { get; }
    public string LastStartedAt { get; }
    public string LastCompletedAt { get; }
    public string LastDuration { get; }
    public string NextRunAt { get; }
    public string LastError { get; }
}
