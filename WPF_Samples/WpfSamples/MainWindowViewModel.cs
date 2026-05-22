using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Wpf.Lib.RTOS;
using WpfSamples.Tests;
using WpfSamples.Samples_RTOS;

namespace WpfSamples;

internal sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly Brush HealthStoppedBackground = new SolidColorBrush(Color.FromRgb(229, 231, 235));
    private static readonly Brush HealthStoppedForeground = new SolidColorBrush(Color.FromRgb(55, 65, 81));
    private static readonly Brush HealthGoodBackground = new SolidColorBrush(Color.FromRgb(220, 252, 231));
    private static readonly Brush HealthGoodForeground = new SolidColorBrush(Color.FromRgb(6, 78, 59));
    private static readonly Brush HealthWarningBackground = new SolidColorBrush(Color.FromRgb(254, 243, 199));
    private static readonly Brush HealthWarningForeground = new SolidColorBrush(Color.FromRgb(120, 53, 15));
    private static readonly Brush HealthMonitoringBackground = new SolidColorBrush(Color.FromRgb(219, 234, 254));
    private static readonly Brush HealthMonitoringForeground = new SolidColorBrush(Color.FromRgb(30, 64, 175));

    private readonly SchedulerService _scheduler = new();
    private SchedulerService? _preemptionTestScheduler;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _preemptionUiTimer;
    private readonly object _snapshotSyncRoot = new();
    private readonly Dictionary<string, ScheduledTaskStatusViewModel> _taskViewModels = [];
    private SchedulerSnapshot? _latestSnapshot;
    private bool _isSnapshotApplyQueued;
    private bool _isRunningTests;
    private bool _isPreemptionTestRunning;
    private string _schedulerState = "Stopped";
    private string _lastSnapshotAt = "-";
    private string _testSummary = "Not run";
    private string _preemptionTestState = "Stopped";
    private string _lastPreemptionEvent = "-";
    private int _preemptionLowWorkUnits;
    private int _preemptionHighRunCount;
    private int _preemptionYieldCount;
    private int _preemptionLowWorkUnitsCounter;
    private int _preemptionHighRunCountCounter;
    private int _preemptionYieldCountCounter;
    private DateTime _lastTrendSecond = DateTime.MinValue;
    private int _lastYieldTotalForTrend;
    private int _preemptionTrendMaxYield = 1;
    private string _preemptionHealthLabel = "중지";
    private string _preemptionHealthDescription = "데모를 시작하면 선점 상태를 분석합니다.";
    private Brush _preemptionHealthBadgeBackground = HealthStoppedBackground;
    private Brush _preemptionHealthBadgeForeground = HealthStoppedForeground;

    public MainWindowViewModel()
    {
        _dispatcher = Application.Current.Dispatcher;
        _preemptionUiTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(120),
        };
        _preemptionUiTimer.Tick += OnPreemptionUiTimerTick;

        StartCommand = new RelayCommand(StartScheduler, () => !_scheduler.IsRunning);
        StopCommand = new RelayCommand(
            () => _ = StopSchedulerAsync(),
            () => _scheduler.IsRunning);
        RunTestsCommand = new RelayCommand(
            () => _ = RunTestsAsync(),
            () => !_isRunningTests && !_isPreemptionTestRunning);
        StartPreemptionTestCommand = new RelayCommand(
            () => _ = StartPreemptionTestAsync(),
            () => !_isPreemptionTestRunning && !_isRunningTests);
        StopPreemptionTestCommand = new RelayCommand(
            () => _ = StopPreemptionTestAsync(),
            () => _isPreemptionTestRunning);

        _scheduler.SnapshotChanged += OnSchedulerSnapshotChanged;
        RegisterSampleTasks();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ScheduledTaskStatusViewModel> Tasks { get; } = [];
    public ObservableCollection<RtosTestResult> TestResults { get; } = [];
    public ObservableCollection<string> PreemptionLogs { get; } = [];
    public ObservableCollection<PreemptionTrendPointViewModel> PreemptionYieldTrend { get; } = [];

    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand RunTestsCommand { get; }
    public RelayCommand StartPreemptionTestCommand { get; }
    public RelayCommand StopPreemptionTestCommand { get; }

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

    public string TestSummary
    {
        get => _testSummary;
        private set
        {
            if (_testSummary == value)
            {
                return;
            }

            _testSummary = value;
            OnPropertyChanged(nameof(TestSummary));
        }
    }

    public string PreemptionTestState
    {
        get => _preemptionTestState;
        private set
        {
            if (_preemptionTestState == value)
            {
                return;
            }

            _preemptionTestState = value;
            OnPropertyChanged(nameof(PreemptionTestState));
        }
    }

    public string LastPreemptionEvent
    {
        get => _lastPreemptionEvent;
        private set
        {
            if (_lastPreemptionEvent == value)
            {
                return;
            }

            _lastPreemptionEvent = value;
            OnPropertyChanged(nameof(LastPreemptionEvent));
        }
    }

    public int PreemptionLowWorkUnits
    {
        get => _preemptionLowWorkUnits;
        private set
        {
            if (_preemptionLowWorkUnits == value)
            {
                return;
            }

            _preemptionLowWorkUnits = value;
            OnPropertyChanged(nameof(PreemptionLowWorkUnits));
        }
    }

    public int PreemptionHighRunCount
    {
        get => _preemptionHighRunCount;
        private set
        {
            if (_preemptionHighRunCount == value)
            {
                return;
            }

            _preemptionHighRunCount = value;
            OnPropertyChanged(nameof(PreemptionHighRunCount));
        }
    }

    public int PreemptionYieldCount
    {
        get => _preemptionYieldCount;
        private set
        {
            if (_preemptionYieldCount == value)
            {
                return;
            }

            _preemptionYieldCount = value;
            OnPropertyChanged(nameof(PreemptionYieldCount));
        }
    }

    public int PreemptionTrendMaxYield
    {
        get => _preemptionTrendMaxYield;
        private set
        {
            var safeValue = Math.Max(1, value);
            if (_preemptionTrendMaxYield == safeValue)
            {
                return;
            }

            _preemptionTrendMaxYield = safeValue;
            OnPropertyChanged(nameof(PreemptionTrendMaxYield));
        }
    }

    public string PreemptionHealthLabel
    {
        get => _preemptionHealthLabel;
        private set
        {
            if (_preemptionHealthLabel == value)
            {
                return;
            }

            _preemptionHealthLabel = value;
            OnPropertyChanged(nameof(PreemptionHealthLabel));
        }
    }

    public string PreemptionHealthDescription
    {
        get => _preemptionHealthDescription;
        private set
        {
            if (_preemptionHealthDescription == value)
            {
                return;
            }

            _preemptionHealthDescription = value;
            OnPropertyChanged(nameof(PreemptionHealthDescription));
        }
    }

    public Brush PreemptionHealthBadgeBackground
    {
        get => _preemptionHealthBadgeBackground;
        private set
        {
            if (ReferenceEquals(_preemptionHealthBadgeBackground, value))
            {
                return;
            }

            _preemptionHealthBadgeBackground = value;
            OnPropertyChanged(nameof(PreemptionHealthBadgeBackground));
        }
    }

    public Brush PreemptionHealthBadgeForeground
    {
        get => _preemptionHealthBadgeForeground;
        private set
        {
            if (ReferenceEquals(_preemptionHealthBadgeForeground, value))
            {
                return;
            }

            _preemptionHealthBadgeForeground = value;
            OnPropertyChanged(nameof(PreemptionHealthBadgeForeground));
        }
    }

    public void Dispose()
    {
        _scheduler.SnapshotChanged -= OnSchedulerSnapshotChanged;
        _preemptionUiTimer.Stop();
        _preemptionUiTimer.Tick -= OnPreemptionUiTimerTick;
        StopPreemptionTestAsync().GetAwaiter().GetResult();
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

    private async Task RunTestsAsync()
    {
        _isRunningTests = true;
        await _dispatcher.InvokeAsync(() =>
        {
            TestSummary = "Running...";
            TestResults.Clear();
            RefreshCommandStates();
        });

        var results = await RtosTestRunner.RunAllAsync().ConfigureAwait(false);

        await _dispatcher.InvokeAsync(() =>
        {
            foreach (var result in results)
            {
                TestResults.Add(result);
            }

            var passedCount = results.Count(result => result.Passed);
            TestSummary = $"{passedCount}/{results.Count} passed";
            _isRunningTests = false;
            RefreshCommandStates();
        });
    }

    private async Task StartPreemptionTestAsync()
    {
        if (_isPreemptionTestRunning)
        {
            return;
        }

        await StopPreemptionTestAsync().ConfigureAwait(false);

        _isPreemptionTestRunning = true;
        Interlocked.Exchange(ref _preemptionLowWorkUnitsCounter, 0);
        Interlocked.Exchange(ref _preemptionHighRunCountCounter, 0);
        Interlocked.Exchange(ref _preemptionYieldCountCounter, 0);
        _lastTrendSecond = DateTime.MinValue;
        _lastYieldTotalForTrend = 0;

        await _dispatcher.InvokeAsync(() =>
        {
            PreemptionLogs.Clear();
            PreemptionYieldTrend.Clear();
            PreemptionTestState = "Running";
            LastPreemptionEvent = "Starting cooperative preemption demo...";
            PreemptionLowWorkUnits = 0;
            PreemptionHighRunCount = 0;
            PreemptionYieldCount = 0;
            PreemptionTrendMaxYield = 1;
            SetPreemptionHealth(
                label: "초기화",
                description: "데모를 시작했고, 첫 실행 데이터를 수집 중입니다.",
                background: HealthMonitoringBackground,
                foreground: HealthMonitoringForeground);
            _preemptionUiTimer.Start();
            RefreshCommandStates();
        });

        var demoScheduler = new SchedulerService(
            tickInterval: TimeSpan.FromMilliseconds(5),
            snapshotInterval: TimeSpan.FromMilliseconds(50));

        demoScheduler.SchedulerError += (_, ex) =>
        {
            _ = _dispatcher.BeginInvoke(() => AppendPreemptionLog($"Scheduler error: {ex.Message}"));
        };

        demoScheduler.Register(new ScheduledTask(
            "Low Priority Worker",
            Enum_TaskPriority.Low,
            TimeSpan.FromMilliseconds(30),
            Enum_TaskExecutionMode.Periodic,
            async (context, cancellationToken) =>
            {
                for (var i = 0; i < 120; i++)
                {
                    if (context.ShouldYield())
                    {
                        Interlocked.Increment(ref _preemptionYieldCountCounter);
                        await _dispatcher.InvokeAsync(() =>
                            AppendPreemptionLog("Low task yielded to higher-priority runnable task.")).Task.ConfigureAwait(false);
                        return;
                    }

                    Interlocked.Increment(ref _preemptionLowWorkUnitsCounter);
                    await Task.Delay(2, cancellationToken).ConfigureAwait(false);
                }

                await _dispatcher.InvokeAsync(() =>
                    AppendPreemptionLog("Low task completed one work slice.")).Task.ConfigureAwait(false);
            },
            statusProvider: () => "Long-running low priority worker",
            overrunPolicy: Enum_TaskOverrunPolicy.FixedDelay));

        demoScheduler.Register(new ScheduledTask(
            "High Priority Urgent",
            Enum_TaskPriority.Critical,
            TimeSpan.FromMilliseconds(90),
            Enum_TaskExecutionMode.Periodic,
            (_, _) =>
            {
                var count = Interlocked.Increment(ref _preemptionHighRunCountCounter);
                _ = _dispatcher.BeginInvoke(() =>
                    AppendPreemptionLog($"High task ran immediately. Count={count}"));
                return Task.CompletedTask;
            },
            statusProvider: () => "Urgent high-priority work"));

        _preemptionTestScheduler = demoScheduler;
        demoScheduler.Start();
    }

    private async Task StopPreemptionTestAsync()
    {
        var demoScheduler = _preemptionTestScheduler;
        _preemptionTestScheduler = null;

        if (demoScheduler is not null)
        {
            try
            {
                await demoScheduler.StopAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            finally
            {
                demoScheduler.Dispose();
            }
        }

        if (_isPreemptionTestRunning)
        {
            _isPreemptionTestRunning = false;

            if (_dispatcher.CheckAccess())
            {
                _preemptionUiTimer.Stop();
                PreemptionTestState = "Stopped";
                LastPreemptionEvent = "Preemption demo stopped.";
                SetPreemptionHealth(
                    label: "중지",
                    description: "데모가 중지되어 상태 분석을 멈췄습니다.",
                    background: HealthStoppedBackground,
                    foreground: HealthStoppedForeground);
                RefreshCommandStates();
            }
            else
            {
                await _dispatcher.InvokeAsync(() =>
                {
                    _preemptionUiTimer.Stop();
                    PreemptionTestState = "Stopped";
                    LastPreemptionEvent = "Preemption demo stopped.";
                    SetPreemptionHealth(
                        label: "중지",
                        description: "데모가 중지되어 상태 분석을 멈췄습니다.",
                        background: HealthStoppedBackground,
                        foreground: HealthStoppedForeground);
                    RefreshCommandStates();
                });
            }
        }
    }

    private void OnPreemptionUiTimerTick(object? sender, EventArgs e)
    {
        PreemptionLowWorkUnits = Volatile.Read(ref _preemptionLowWorkUnitsCounter);
        PreemptionHighRunCount = Volatile.Read(ref _preemptionHighRunCountCounter);
        PreemptionYieldCount = Volatile.Read(ref _preemptionYieldCountCounter);
        UpdatePreemptionHealthStatus();

        var nowSecond = DateTime.Now;
        nowSecond = new DateTime(
            nowSecond.Year,
            nowSecond.Month,
            nowSecond.Day,
            nowSecond.Hour,
            nowSecond.Minute,
            nowSecond.Second,
            nowSecond.Kind);

        if (nowSecond == _lastTrendSecond)
        {
            return;
        }

        var currentYieldTotal = Volatile.Read(ref _preemptionYieldCountCounter);
        var deltaYield = Math.Max(0, currentYieldTotal - _lastYieldTotalForTrend);
        _lastYieldTotalForTrend = currentYieldTotal;
        _lastTrendSecond = nowSecond;

        PreemptionYieldTrend.Add(new PreemptionTrendPointViewModel(
            nowSecond.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            deltaYield));

        while (PreemptionYieldTrend.Count > 10)
        {
            PreemptionYieldTrend.RemoveAt(0);
        }

        PreemptionTrendMaxYield = PreemptionYieldTrend.Count == 0
            ? 1
            : PreemptionYieldTrend.Max(item => item.YieldCount);
    }

    private void AppendPreemptionLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        LastPreemptionEvent = message;
        PreemptionLogs.Add(line);

        while (PreemptionLogs.Count > 200)
        {
            PreemptionLogs.RemoveAt(0);
        }
    }

    private void UpdatePreemptionHealthStatus()
    {
        if (!_isPreemptionTestRunning)
        {
            SetPreemptionHealth(
                label: "중지",
                description: "데모를 시작하면 선점 상태를 분석합니다.",
                background: HealthStoppedBackground,
                foreground: HealthStoppedForeground);
            return;
        }

        if (PreemptionHighRunCount < 2)
        {
            SetPreemptionHealth(
                label: "초기화",
                description: "아직 실행 횟수가 적어서 판단을 보류합니다.",
                background: HealthMonitoringBackground,
                foreground: HealthMonitoringForeground);
            return;
        }

        if (PreemptionYieldCount >= 3)
        {
            SetPreemptionHealth(
                label: "정상",
                description: "양보가 반복 발생하여 협력형 선점이 잘 작동 중입니다.",
                background: HealthGoodBackground,
                foreground: HealthGoodForeground);
            return;
        }

        if (PreemptionLowWorkUnits >= 120 && PreemptionYieldCount == 0)
        {
            SetPreemptionHealth(
                label: "주의",
                description: "낮은 우선순위 작업량만 증가합니다. 양보 호출 지점을 점검하세요.",
                background: HealthWarningBackground,
                foreground: HealthWarningForeground);
            return;
        }

        SetPreemptionHealth(
            label: "관찰",
            description: "실행 데이터가 누적되는 중입니다. 추세를 조금 더 관찰하세요.",
            background: HealthMonitoringBackground,
            foreground: HealthMonitoringForeground);
    }

    private void SetPreemptionHealth(string label, string description, Brush background, Brush foreground)
    {
        PreemptionHealthLabel = label;
        PreemptionHealthDescription = description;
        PreemptionHealthBadgeBackground = background;
        PreemptionHealthBadgeForeground = foreground;
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
        RunTestsCommand.RaiseCanExecuteChanged();
        StartPreemptionTestCommand.RaiseCanExecuteChanged();
        StopPreemptionTestCommand.RaiseCanExecuteChanged();
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    internal sealed class PreemptionTrendPointViewModel
    {
        public PreemptionTrendPointViewModel(string secondLabel, int yieldCount)
        {
            SecondLabel = secondLabel;
            YieldCount = yieldCount;
        }

        public string SecondLabel { get; }
        public int YieldCount { get; }
    }
}
