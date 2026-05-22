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
    private readonly object _snapshotSyncRoot = new();
    private readonly Dictionary<string, ScheduledTaskStatusViewModel> _taskViewModels = [];
    private SchedulerSnapshot? _latestSnapshot;
    private bool _isSnapshotApplyQueued;
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
        lock (_snapshotSyncRoot)
        {
            _latestSnapshot = snapshot;

            if (_isSnapshotApplyQueued)
            {
                return;
            }

            _isSnapshotApplyQueued = true;
        }

        _dispatcher.BeginInvoke(ApplyLatestSnapshot, DispatcherPriority.Background);
    }

    private void ApplyLatestSnapshot()
    {
        SchedulerSnapshot? snapshot;

        lock (_snapshotSyncRoot)
        {
            snapshot = _latestSnapshot;
            _latestSnapshot = null;
            _isSnapshotApplyQueued = false;
        }

        if (snapshot is null)
        {
            return;
        }

        SchedulerState = snapshot.IsRunning ? "Running" : "Stopped";
        LastSnapshotAt = snapshot.CreatedAt.LocalDateTime.ToString("HH:mm:ss.fff");

        foreach (var task in snapshot.Tasks)
        {
            if (_taskViewModels.TryGetValue(task.Name, out var taskViewModel))
            {
                taskViewModel.Update(task);
                continue;
            }

            taskViewModel = new ScheduledTaskStatusViewModel(task);
            _taskViewModels[task.Name] = taskViewModel;
            Tasks.Add(taskViewModel);
        }

        RefreshCommandStates();
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
