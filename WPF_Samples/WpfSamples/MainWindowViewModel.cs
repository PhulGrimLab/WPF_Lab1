using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Wpf.Lib.RTOS;
using WpfSamples.Samples_RTOS;

namespace WpfSamples;

internal sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly SchedulerService _scheduler = new();
    private readonly Dispatcher _dispatcher;
    private string _schedulerState = "Stopped";
    private string _lastSnapshotAt = "-";

    public MainWindowViewModel()
    {
        _dispatcher = Application.Current.Dispatcher;

        StartCommand = new RelayCommand(StartScheduler, () => !_scheduler.IsRunning);
        StopCommand = new RelayCommand(
            () => _ = StopSchedulerAsync(),
            () => _scheduler.IsRunning);

        _scheduler.SnapshotChanged += OnSchedulerSnapshotChanged;
        RegisterSampleTasks();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ScheduledTaskStatusViewModel> Tasks { get; } = [];

    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }

    public string SchedulerState
    {
        get => _schedulerState;
        private set
        {
            if (_schedulerState == value)
            {
                return;
            }

            _schedulerState = value;
            OnPropertyChanged(nameof(SchedulerState));
        }
    }

    public string LastSnapshotAt
    {
        get => _lastSnapshotAt;
        private set
        {
            if (_lastSnapshotAt == value)
            {
                return;
            }

            _lastSnapshotAt = value;
            OnPropertyChanged(nameof(LastSnapshotAt));
        }
    }

    public void Dispose()
    {
        _scheduler.SnapshotChanged -= OnSchedulerSnapshotChanged;
        _scheduler.Dispose();
    }

    private void RegisterSampleTasks()
    {
        _scheduler.Register(new CounterTask());
        _scheduler.Register(new ClockTask());
        _scheduler.Register(new UiRefreshTask());
    }

    private void StartScheduler()
    {
        _scheduler.Start();
        RefreshCommandStates();
    }

    private async Task StopSchedulerAsync()
    {
        await _scheduler.StopAsync().ConfigureAwait(false);

        await _dispatcher.InvokeAsync(RefreshCommandStates);
    }

    private void OnSchedulerSnapshotChanged(object? sender, SchedulerSnapshot snapshot)
    {
        _dispatcher.BeginInvoke(() =>
        {
            SchedulerState = snapshot.IsRunning ? "Running" : "Stopped";
            LastSnapshotAt = snapshot.CreatedAt.LocalDateTime.ToString("HH:mm:ss.fff");

            Tasks.Clear();
            foreach (var task in snapshot.Tasks)
            {
                Tasks.Add(new ScheduledTaskStatusViewModel(task));
            }

            RefreshCommandStates();
        });
    }

    private void RefreshCommandStates()
    {
        StartCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
