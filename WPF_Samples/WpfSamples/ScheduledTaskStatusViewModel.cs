using System.ComponentModel;
using Wpf.Lib.RTOS;

namespace WpfSamples;

internal sealed class ScheduledTaskStatusViewModel : INotifyPropertyChanged
{
    private string _priority = string.Empty;
    private string _period = string.Empty;
    private string _mode = string.Empty;
    private string _state = string.Empty;
    private bool _isEnabled;
    private string _status = string.Empty;
    private long _runCount;
    private string _lastStartedAt = "-";
    private string _lastCompletedAt = "-";
    private string _lastDuration = "-";
    private string _nextRunAt = "-";
    private string _lastError = "-";
    private string _lastStartedThreadId = "-";
    private string _lastCompletedThreadId = "-";

    public ScheduledTaskStatusViewModel(ScheduledTaskSnapshot snapshot)
    {
        Name = snapshot.Name;
        Update(snapshot);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; }

    public string Priority
    {
        get => _priority;
        private set => SetProperty(ref _priority, value, nameof(Priority));
    }

    public string Period
    {
        get => _period;
        private set => SetProperty(ref _period, value, nameof(Period));
    }

    public string Mode
    {
        get => _mode;
        private set => SetProperty(ref _mode, value, nameof(Mode));
    }

    public string State
    {
        get => _state;
        private set => SetProperty(ref _state, value, nameof(State));
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        private set => SetProperty(ref _isEnabled, value, nameof(IsEnabled));
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value, nameof(Status));
    }

    public long RunCount
    {
        get => _runCount;
        private set => SetProperty(ref _runCount, value, nameof(RunCount));
    }

    public string LastStartedAt
    {
        get => _lastStartedAt;
        private set => SetProperty(ref _lastStartedAt, value, nameof(LastStartedAt));
    }

    public string LastCompletedAt
    {
        get => _lastCompletedAt;
        private set => SetProperty(ref _lastCompletedAt, value, nameof(LastCompletedAt));
    }

    public string LastDuration
    {
        get => _lastDuration;
        private set => SetProperty(ref _lastDuration, value, nameof(LastDuration));
    }

    public string NextRunAt
    {
        get => _nextRunAt;
        private set => SetProperty(ref _nextRunAt, value, nameof(NextRunAt));
    }

    public string LastError
    {
        get => _lastError;
        private set => SetProperty(ref _lastError, value, nameof(LastError));
    }

    public string LastStartedThreadId
    {
        get => _lastStartedThreadId;
        private set => SetProperty(ref _lastStartedThreadId, value, nameof(LastStartedThreadId));
    }

    public string LastCompletedThreadId
    {
        get => _lastCompletedThreadId;
        private set => SetProperty(ref _lastCompletedThreadId, value, nameof(LastCompletedThreadId));
    }

    public void Update(ScheduledTaskSnapshot snapshot)
    {
        Priority = snapshot.Priority.ToString();
        Period = $"{snapshot.Period.TotalMilliseconds:N0} ms";
        Mode = snapshot.Mode.ToString();
        State = snapshot.State.ToString();
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
        LastStartedThreadId = snapshot.LastStartedThreadId?.ToString() ?? "-";
        LastCompletedThreadId = snapshot.LastCompletedThreadId?.ToString() ?? "-";
    }

    private void SetProperty<T>(ref T field, T value, string propertyName)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
