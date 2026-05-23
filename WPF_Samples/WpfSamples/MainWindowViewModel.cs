using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Wpf.Lib.RTOS;
using WpfSamples.Tests;

namespace WpfSamples;

internal sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable, IAsyncDisposable
{
    private static readonly Brush HealthStoppedBackground = new SolidColorBrush(Color.FromRgb(229, 231, 235));
    private static readonly Brush HealthStoppedForeground = new SolidColorBrush(Color.FromRgb(55, 65, 81));
    private static readonly Brush HealthGoodBackground = new SolidColorBrush(Color.FromRgb(220, 252, 231));
    private static readonly Brush HealthGoodForeground = new SolidColorBrush(Color.FromRgb(6, 78, 59));
    private static readonly Brush HealthWarningBackground = new SolidColorBrush(Color.FromRgb(254, 243, 199));
    private static readonly Brush HealthWarningForeground = new SolidColorBrush(Color.FromRgb(120, 53, 15));
    private static readonly Brush HealthMonitoringBackground = new SolidColorBrush(Color.FromRgb(219, 234, 254));
    private static readonly Brush HealthMonitoringForeground = new SolidColorBrush(Color.FromRgb(30, 64, 175));
    private static readonly Brush FlowActiveBrush = new SolidColorBrush(Color.FromRgb(22, 163, 74));
    private static readonly Brush FlowIdleBrush = new SolidColorBrush(Color.FromRgb(209, 213, 219));
    private static readonly Brush InspectorIdleBackground = new SolidColorBrush(Color.FromRgb(248, 250, 252));
    private static readonly Brush InspectorIdleBorder = new SolidColorBrush(Color.FromRgb(226, 232, 240));
    private static readonly Brush InspectorLowActiveBackground = new SolidColorBrush(Color.FromRgb(220, 252, 231));
    private static readonly Brush InspectorLowActiveBorder = new SolidColorBrush(Color.FromRgb(134, 239, 172));
    private static readonly Brush InspectorHighActiveBackground = new SolidColorBrush(Color.FromRgb(254, 226, 226));
    private static readonly Brush InspectorHighActiveBorder = new SolidColorBrush(Color.FromRgb(252, 165, 165));
    private static readonly Brush InspectorCurrentActiveBackground = new SolidColorBrush(Color.FromRgb(219, 234, 254));
    private static readonly Brush InspectorCurrentActiveBorder = new SolidColorBrush(Color.FromRgb(147, 197, 253));
    private static readonly Brush DiningThinkingBackground = new SolidColorBrush(Color.FromRgb(239, 246, 255));
    private static readonly Brush DiningThinkingAccent = new SolidColorBrush(Color.FromRgb(37, 99, 235));
    private static readonly Brush DiningHungryBackground = new SolidColorBrush(Color.FromRgb(255, 251, 235));
    private static readonly Brush DiningHungryAccent = new SolidColorBrush(Color.FromRgb(217, 119, 6));
    private static readonly Brush DiningEatingBackground = new SolidColorBrush(Color.FromRgb(220, 252, 231));
    private static readonly Brush DiningEatingAccent = new SolidColorBrush(Color.FromRgb(22, 163, 74));
    private static readonly Brush DiningIdleBackground = new SolidColorBrush(Color.FromRgb(248, 250, 252));
    private static readonly Brush DiningIdleAccent = new SolidColorBrush(Color.FromRgb(100, 116, 139));
    private static readonly Brush DiningForkAvailableBrush = new SolidColorBrush(Color.FromRgb(209, 250, 229));
    private static readonly Brush DiningForkHeldBrush = new SolidColorBrush(Color.FromRgb(254, 226, 226));

    private readonly SchedulerMonitorDemo _monitorDemo = new();
    private readonly DiningPhilosophersDemo _diningDemo = new();
    private readonly PreemptionDemo _preemptionDemo = new();
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _preemptionUiTimer;
    private readonly DispatcherTimer _diningUiTimer;
    private readonly object _snapshotSyncRoot = new();
    private readonly object _preemptionSnapshotSyncRoot = new();
    private readonly Dictionary<string, ScheduledTaskStatusViewModel> _taskViewModels = [];
    private readonly Dictionary<string, ScheduledTaskStatusViewModel> _preemptionTaskViewModels = [];
    private readonly Dictionary<string, ScheduledTaskStatusViewModel> _diningTaskViewModels = [];
    private readonly Dictionary<string, LowWorkerYieldStatsViewModel> _lowWorkerYieldViewModels = [];
    private readonly HashSet<string> _mainActiveTaskNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _preemptionActiveTaskNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _diningActiveTaskNames = new(StringComparer.Ordinal);
    private SchedulerSnapshot? _latestSnapshot;
    private SchedulerSnapshot? _latestPreemptionSnapshot;
    private bool _isSnapshotApplyQueued;
    private bool _isRunningTests;
    private bool _isPreemptionTestRunning;
    private bool _isDiningRunning;
    private string _schedulerState = "Stopped";
    private string _lastSnapshotAt = "-";
    private string _testSummary = "Not run";
    private string _diningState = "중지";
    private string _diningElapsed = "-";
    private string _diningSummary = "시작하면 5명의 철학자가 생각하기, 배고픔, 식사 상태를 반복합니다.";
    private string _diningLastEvent = "-";
    private string _diningConcurrentEatingStatus = "동시 식사: 0명 (최대 0명)";
    private int _diningMaxConcurrentEating;
    private DateTimeOffset _diningSessionStartedAt = DateTimeOffset.MinValue;
    private string _preemptionTestState = "Stopped";
    private string _lastPreemptionEvent = "-";
    private string _preemptionHighLastInterval = "-";
    private int _preemptionLowWorkUnits;
    private int _preemptionHighRunCount;
    private int _preemptionYieldCount;
    private int _preemptionLowLastThreadId;
    private int _preemptionHighLastThreadId;
    private int _preemptionCurrentExecutionThreadId;
    private int _preemptionTrendMaxYield = 1;
    private string _preemptionHealthLabel = "중지";
    private string _preemptionHealthDescription = "데모를 시작하면 선점 상태를 분석합니다.";
    private string _preemptionStarvationStatus = "기아 감지: 중지";
    private double _preemptionStarvationLowStallSeconds = 3.0;
    private Brush _preemptionStarvationForeground = HealthStoppedForeground;
    private Brush _preemptionHealthBadgeBackground = HealthStoppedBackground;
    private Brush _preemptionHealthBadgeForeground = HealthStoppedForeground;
    private string _preemptionFlowHeadline = "데모를 시작하면 선점 흐름을 단계별로 표시합니다.";
    private string _preemptionFlowDetail = "LOW 작업, HIGH 실행 가능, LOW 양보, HIGH 선실행 순서를 자동으로 추적합니다.";
    private Brush _preemptionLowStepBrush = FlowIdleBrush;
    private Brush _preemptionYieldStepBrush = FlowIdleBrush;
    private Brush _preemptionHighStepBrush = FlowIdleBrush;

    private string _preemptionActiveTask = "대기 중";
    private Brush _preemptionLowInspectorBackground = InspectorIdleBackground;
    private Brush _preemptionLowInspectorBorder = InspectorIdleBorder;
    private Brush _preemptionHighInspectorBackground = InspectorIdleBackground;
    private Brush _preemptionHighInspectorBorder = InspectorIdleBorder;
    private Brush _preemptionCurrentInspectorBackground = InspectorIdleBackground;
    private Brush _preemptionCurrentInspectorBorder = InspectorIdleBorder;

    public MainWindowViewModel()
    {
        _dispatcher = Application.Current.Dispatcher;
        _preemptionUiTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(80),
        };
        _preemptionUiTimer.Tick += OnPreemptionUiTimerTick;
        _diningUiTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };
        _diningUiTimer.Tick += OnDiningUiTimerTick;

        StartCommand = new RelayCommand(StartScheduler, () => !_monitorDemo.IsRunning);

        StopCommand = new RelayCommand(
            () => _ = StopSchedulerAsync(),
            () => _monitorDemo.IsRunning);

        RunTestsCommand = new RelayCommand(
            () => _ = RunTestsAsync(),
            () => !_isRunningTests && !_isPreemptionTestRunning);

        StartPreemptionTestCommand = new RelayCommand(
            () => _ = StartPreemptionTestAsync(),
            () => !_isPreemptionTestRunning && !_isRunningTests);

        StopPreemptionTestCommand = new RelayCommand(
            () => _ = StopPreemptionTestAsync(),
            () => _isPreemptionTestRunning);

        StartDiningCommand = new RelayCommand(
            StartDiningDemo,
            () => !_isDiningRunning && !_isRunningTests);

        StopDiningCommand = new RelayCommand(
            () => _ = StopDiningDemoAsync(),
            () => _isDiningRunning);

        ResetDiningCommand = new RelayCommand(
            ResetDiningDemo,
            () => !_isDiningRunning);

        _monitorDemo.SnapshotChanged += OnSchedulerSnapshotChanged;
        _diningDemo.SnapshotChanged += OnDiningDemoSnapshotChanged;
        _diningDemo.SchedulerSnapshotChanged += OnDiningSchedulerSnapshotChanged;
        _preemptionDemo.SchedulerSnapshotChanged += OnPreemptionSchedulerSnapshotChanged;

        ApplyLatestSnapshot(_monitorDemo.CurrentSnapshot);
        ApplyDiningSnapshot(_diningDemo.CurrentSnapshot);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ScheduledTaskStatusViewModel> Tasks { get; } = [];
    public ObservableCollection<ScheduledTaskStatusViewModel> ActiveTasks { get; } = [];
    public ObservableCollection<RtosTestResult> TestResults { get; } = [];
    public ObservableCollection<string> PreemptionLogs { get; } = [];
    public ObservableCollection<PreemptionTrendPointViewModel> PreemptionYieldTrend { get; } = [];
    public ObservableCollection<LowWorkerYieldStatsViewModel> PreemptionLowWorkerYields { get; } = [];
    public ObservableCollection<DiningPhilosopherViewModel> DiningPhilosophers { get; } = [];
    public ObservableCollection<DiningForkViewModel> DiningForks { get; } = [];
    public ObservableCollection<string> DiningLogs { get; } = [];

    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand RunTestsCommand { get; }
    public RelayCommand StartPreemptionTestCommand { get; }
    public RelayCommand StopPreemptionTestCommand { get; }
    public RelayCommand StartDiningCommand { get; }
    public RelayCommand StopDiningCommand { get; }
    public RelayCommand ResetDiningCommand { get; }

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

    public string DiningState
    {
        get => _diningState;
        private set
        {
            if (_diningState == value)
            {
                return;
            }

            _diningState = value;
            OnPropertyChanged(nameof(DiningState));
        }
    }

    public string DiningElapsed
    {
        get => _diningElapsed;
        private set
        {
            if (_diningElapsed == value)
            {
                return;
            }

            _diningElapsed = value;
            OnPropertyChanged(nameof(DiningElapsed));
        }
    }

    public string DiningSummary
    {
        get => _diningSummary;
        private set
        {
            if (_diningSummary == value)
            {
                return;
            }

            _diningSummary = value;
            OnPropertyChanged(nameof(DiningSummary));
        }
    }

    public string DiningLastEvent
    {
        get => _diningLastEvent;
        private set
        {
            if (_diningLastEvent == value)
            {
                return;
            }

            _diningLastEvent = value;
            OnPropertyChanged(nameof(DiningLastEvent));
        }
    }

    public string DiningConcurrentEatingStatus
    {
        get => _diningConcurrentEatingStatus;
        private set
        {
            if (_diningConcurrentEatingStatus == value)
            {
                return;
            }

            _diningConcurrentEatingStatus = value;
            OnPropertyChanged(nameof(DiningConcurrentEatingStatus));
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

    public string PreemptionHighLastInterval
    {
        get => _preemptionHighLastInterval;
        private set
        {
            if (_preemptionHighLastInterval == value)
            {
                return;
            }

            _preemptionHighLastInterval = value;
            OnPropertyChanged(nameof(PreemptionHighLastInterval));
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

    public int PreemptionLowLastThreadId
    {
        get => _preemptionLowLastThreadId;
        private set
        {
            if (_preemptionLowLastThreadId == value)
            {
                return;
            }

            _preemptionLowLastThreadId = value;
            OnPropertyChanged(nameof(PreemptionLowLastThreadId));
        }
    }

    public int PreemptionHighLastThreadId
    {
        get => _preemptionHighLastThreadId;
        private set
        {
            if (_preemptionHighLastThreadId == value)
            {
                return;
            }

            _preemptionHighLastThreadId = value;
            OnPropertyChanged(nameof(PreemptionHighLastThreadId));
        }
    }

    public int PreemptionUiThreadId => _dispatcher.Thread.ManagedThreadId;

    public int PreemptionCurrentExecutionThreadId
    {
        get => _preemptionCurrentExecutionThreadId;
        private set
        {
            if (_preemptionCurrentExecutionThreadId == value)
            {
                return;
            }

            _preemptionCurrentExecutionThreadId = value;
            OnPropertyChanged(nameof(PreemptionCurrentExecutionThreadId));
            OnPropertyChanged(nameof(PreemptionCurrentExecutionThreadLabel));
        }
    }

    public string PreemptionCurrentExecutionThreadLabel =>
        PreemptionCurrentExecutionThreadId > 0
            ? PreemptionCurrentExecutionThreadId.ToString(CultureInfo.InvariantCulture)
            : "없음";

    public string PreemptionActiveTask
    {
        get => _preemptionActiveTask;
        private set
        {
            if (_preemptionActiveTask == value)
            {
                return;
            }

            _preemptionActiveTask = value;
            OnPropertyChanged(nameof(PreemptionActiveTask));
        }
    }

    public Brush PreemptionLowInspectorBackground
    {
        get => _preemptionLowInspectorBackground;
        private set
        {
            if (ReferenceEquals(_preemptionLowInspectorBackground, value))
            {
                return;
            }

            _preemptionLowInspectorBackground = value;
            OnPropertyChanged(nameof(PreemptionLowInspectorBackground));
        }
    }

    public Brush PreemptionLowInspectorBorder
    {
        get => _preemptionLowInspectorBorder;
        private set
        {
            if (ReferenceEquals(_preemptionLowInspectorBorder, value))
            {
                return;
            }

            _preemptionLowInspectorBorder = value;
            OnPropertyChanged(nameof(PreemptionLowInspectorBorder));
        }
    }

    public Brush PreemptionHighInspectorBackground
    {
        get => _preemptionHighInspectorBackground;
        private set
        {
            if (ReferenceEquals(_preemptionHighInspectorBackground, value))
            {
                return;
            }

            _preemptionHighInspectorBackground = value;
            OnPropertyChanged(nameof(PreemptionHighInspectorBackground));
        }
    }

    public Brush PreemptionHighInspectorBorder
    {
        get => _preemptionHighInspectorBorder;
        private set
        {
            if (ReferenceEquals(_preemptionHighInspectorBorder, value))
            {
                return;
            }

            _preemptionHighInspectorBorder = value;
            OnPropertyChanged(nameof(PreemptionHighInspectorBorder));
        }
    }

    public Brush PreemptionCurrentInspectorBackground
    {
        get => _preemptionCurrentInspectorBackground;
        private set
        {
            if (ReferenceEquals(_preemptionCurrentInspectorBackground, value))
            {
                return;
            }

            _preemptionCurrentInspectorBackground = value;
            OnPropertyChanged(nameof(PreemptionCurrentInspectorBackground));
        }
    }

    public Brush PreemptionCurrentInspectorBorder
    {
        get => _preemptionCurrentInspectorBorder;
        private set
        {
            if (ReferenceEquals(_preemptionCurrentInspectorBorder, value))
            {
                return;
            }

            _preemptionCurrentInspectorBorder = value;
            OnPropertyChanged(nameof(PreemptionCurrentInspectorBorder));
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

    public int PreemptionLowTaskCount => 3;     // xaml에서 바인딩
    public int PreemptionHighTaskCount => 2;    // xaml에서 바인딩
    public string PreemptionLowTaskName => "Low Workers A/B/C";
    public string PreemptionHighTaskName => "Normal Telemetry + High Urgent";

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

    public string PreemptionStarvationStatus
    {
        get => _preemptionStarvationStatus;
        private set
        {
            if (_preemptionStarvationStatus == value)
            {
                return;
            }

            _preemptionStarvationStatus = value;
            OnPropertyChanged(nameof(PreemptionStarvationStatus));
        }
    }

    public double PreemptionStarvationLowStallSeconds
    {
        get => _preemptionStarvationLowStallSeconds;
        set
        {
            var clamped = Math.Clamp(value, 1.0, 8.0);
            if (Math.Abs(_preemptionStarvationLowStallSeconds - clamped) < 0.01)
            {
                return;
            }

            _preemptionStarvationLowStallSeconds = clamped;
            _preemptionDemo.StarvationLowStallSeconds = clamped;
            OnPropertyChanged(nameof(PreemptionStarvationLowStallSeconds));
            OnPropertyChanged(nameof(PreemptionStarvationLowStallLabel));
        }
    }

    public string PreemptionStarvationLowStallLabel =>
        $"기아 판정 지연: {PreemptionStarvationLowStallSeconds:N1}초";

    public Brush PreemptionStarvationForeground
    {
        get => _preemptionStarvationForeground;
        private set
        {
            if (ReferenceEquals(_preemptionStarvationForeground, value))
            {
                return;
            }

            _preemptionStarvationForeground = value;
            OnPropertyChanged(nameof(PreemptionStarvationForeground));
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

    public string PreemptionFlowHeadline
    {
        get => _preemptionFlowHeadline;
        private set
        {
            if (_preemptionFlowHeadline == value)
            {
                return;
            }

            _preemptionFlowHeadline = value;
            OnPropertyChanged(nameof(PreemptionFlowHeadline));
        }
    }

    public string PreemptionFlowDetail
    {
        get => _preemptionFlowDetail;
        private set
        {
            if (_preemptionFlowDetail == value)
            {
                return;
            }

            _preemptionFlowDetail = value;
            OnPropertyChanged(nameof(PreemptionFlowDetail));
        }
    }

    public Brush PreemptionLowStepBrush
    {
        get => _preemptionLowStepBrush;
        private set
        {
            if (ReferenceEquals(_preemptionLowStepBrush, value))
            {
                return;
            }

            _preemptionLowStepBrush = value;
            OnPropertyChanged(nameof(PreemptionLowStepBrush));
        }
    }

    public Brush PreemptionYieldStepBrush
    {
        get => _preemptionYieldStepBrush;
        private set
        {
            if (ReferenceEquals(_preemptionYieldStepBrush, value))
            {
                return;
            }

            _preemptionYieldStepBrush = value;
            OnPropertyChanged(nameof(PreemptionYieldStepBrush));
        }
    }

    public Brush PreemptionHighStepBrush
    {
        get => _preemptionHighStepBrush;
        private set
        {
            if (ReferenceEquals(_preemptionHighStepBrush, value))
            {
                return;
            }

            _preemptionHighStepBrush = value;
            OnPropertyChanged(nameof(PreemptionHighStepBrush));
        }
    }

    public void Dispose()
    {
        if (_dispatcher.CheckAccess())
        {
            _ = DisposeAsync();
            return;
        }

        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        _monitorDemo.SnapshotChanged -= OnSchedulerSnapshotChanged;
        _diningDemo.SnapshotChanged -= OnDiningDemoSnapshotChanged;
        _diningDemo.SchedulerSnapshotChanged -= OnDiningSchedulerSnapshotChanged;
        _preemptionDemo.SchedulerSnapshotChanged -= OnPreemptionSchedulerSnapshotChanged;
        _preemptionUiTimer.Stop();
        _preemptionUiTimer.Tick -= OnPreemptionUiTimerTick;
        _diningUiTimer.Stop();
        _diningUiTimer.Tick -= OnDiningUiTimerTick;
        await StopDiningDemoAsync().ConfigureAwait(false);
        await StopPreemptionTestAsync().ConfigureAwait(false);
        await _diningDemo.DisposeAsync().ConfigureAwait(false);
        await _preemptionDemo.DisposeAsync().ConfigureAwait(false);
        await _monitorDemo.DisposeAsync().ConfigureAwait(false);
    }

    private void StartScheduler()
    {
        _monitorDemo.Start();
        RefreshCommandStates();
    }

    private async Task StopSchedulerAsync()
    {
        _ = await _monitorDemo.StopAsync().ConfigureAwait(false);

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

    private void StartDiningDemo()
    {
        _diningDemo.Start();
        _isDiningRunning = true;
        _diningUiTimer.Start();
        RefreshCommandStates();
    }

    private async Task StopDiningDemoAsync()
    {
        var stopped = await _diningDemo.StopAsync().ConfigureAwait(false);
        if (!stopped)
        {
            await _dispatcher.InvokeAsync(() =>
            {
                ApplyDiningSnapshot(_diningDemo.CurrentSnapshot);
                RefreshCommandStates();
            });
            return;
        }

        _isDiningRunning = false;
        _diningUiTimer.Stop();
        await _dispatcher.InvokeAsync(() =>
        {
            ApplyDiningSnapshot(_diningDemo.CurrentSnapshot);
            ClearDiningActiveTasks();
            RefreshCommandStates();
        });
    }

    private void ResetDiningDemo()
    {
        _diningDemo.Reset();
        _isDiningRunning = _diningDemo.IsRunning;
        ApplyDiningSnapshot(_diningDemo.CurrentSnapshot);
        RefreshCommandStates();
    }

    private void OnDiningUiTimerTick(object? sender, EventArgs e)
    {
        ApplyDiningSnapshot(_diningDemo.CurrentSnapshot);
    }

    private void OnDiningDemoSnapshotChanged(object? sender, DiningPhilosophersSnapshot snapshot)
    {
        _dispatcher.BeginInvoke(() => ApplyDiningSnapshot(snapshot), DispatcherPriority.Background);
    }

    private void OnDiningSchedulerSnapshotChanged(object? sender, SchedulerSnapshot snapshot)
    {
        _dispatcher.BeginInvoke(() => ApplyDiningSchedulerSnapshot(snapshot), DispatcherPriority.Background);
    }

    private void ApplyDiningSchedulerSnapshot(SchedulerSnapshot snapshot)
    {
        foreach (var task in snapshot.Tasks)
        {
            if (_diningTaskViewModels.TryGetValue(task.Name, out var taskViewModel))
            {
                taskViewModel.Update(task);
                continue;
            }

            _diningTaskViewModels[task.Name] = new ScheduledTaskStatusViewModel(task);
        }

        SyncActiveTasks(snapshot, _diningActiveTaskNames);
    }

    private void ApplyDiningSnapshot(DiningPhilosophersSnapshot snapshot)
    {
        _isDiningRunning = snapshot.IsRunning;
        DiningState = snapshot.IsRunning ? "실행 중" : "중지";
        DiningElapsed = snapshot.IsRunning && snapshot.StartedAt != DateTimeOffset.MinValue
            ? (DateTimeOffset.Now - snapshot.StartedAt).ToString(@"mm\:ss\.f", CultureInfo.InvariantCulture)
            : "-";

        UpdateDiningConcurrentEatingStatus(snapshot);

        SyncDiningPhilosophers(snapshot.Philosophers);
        SyncDiningForks(snapshot.Forks);
        SyncDiningLogs(snapshot.Logs);
        DiningSummary = BuildDiningSummary(snapshot);
        DiningLastEvent = snapshot.Logs.Count > 0 ? StripTimestamp(snapshot.Logs[0]) : "-";
    }

    private void UpdateDiningConcurrentEatingStatus(DiningPhilosophersSnapshot snapshot)
    {
        if (snapshot.StartedAt != _diningSessionStartedAt)
        {
            _diningSessionStartedAt = snapshot.StartedAt;
            _diningMaxConcurrentEating = 0;
        }

        var currentConcurrentEating = snapshot.Philosophers.Count(item => item.State == DiningPhilosopherState.Eating);
        if (currentConcurrentEating > _diningMaxConcurrentEating)
        {
            _diningMaxConcurrentEating = currentConcurrentEating;
        }

        DiningConcurrentEatingStatus = $"동시 식사: {currentConcurrentEating}명 (최대 {_diningMaxConcurrentEating}명)";
    }

    private void SyncDiningPhilosophers(IReadOnlyList<DiningPhilosopherSnapshot> philosophers)
    {
        while (DiningPhilosophers.Count < philosophers.Count)
        {
            var index = DiningPhilosophers.Count;
            DiningPhilosophers.Add(new DiningPhilosopherViewModel(index, $"철학자 {index + 1}"));
        }

        while (DiningPhilosophers.Count > philosophers.Count)
        {
            DiningPhilosophers.RemoveAt(DiningPhilosophers.Count - 1);
        }

        for (var i = 0; i < philosophers.Count; i++)
        {
            DiningPhilosophers[i].Update(philosophers[i], GetDiningBackground(philosophers[i].State), GetDiningAccent(philosophers[i].State));
        }
    }

    private void SyncDiningForks(IReadOnlyList<DiningForkSnapshot> forks)
    {
        while (DiningForks.Count < forks.Count)
        {
            var index = DiningForks.Count;
            DiningForks.Add(new DiningForkViewModel(index, $"포크 {index + 1}"));
        }

        while (DiningForks.Count > forks.Count)
        {
            DiningForks.RemoveAt(DiningForks.Count - 1);
        }

        for (var i = 0; i < forks.Count; i++)
        {
            if (forks[i].IsHeld)
            {
                DiningForks[i].SetHeld(forks[i].Holder);
            }
            else
            {
                DiningForks[i].SetAvailable();
            }
        }
    }

    private void SyncDiningLogs(IReadOnlyList<string> logs)
    {
        DiningLogs.Clear();
        foreach (var log in logs)
        {
            DiningLogs.Add(log);
        }
    }

    private static string BuildDiningSummary(DiningPhilosophersSnapshot snapshot)
    {
        var thinking = snapshot.Philosophers.Count(item => item.State == DiningPhilosopherState.Thinking);
        var hungry = snapshot.Philosophers.Count(item => item.State == DiningPhilosopherState.Hungry);
        var eating = snapshot.Philosophers.Count(item => item.State == DiningPhilosopherState.Eating);
        var meals = snapshot.Philosophers.Sum(item => item.MealCount);
        return $"생각 {thinking}명, 대기 {hungry}명, 식사 {eating}명, 총 식사 {meals}회";
    }

    private static Brush GetDiningBackground(DiningPhilosopherState state)
    {
        return state switch
        {
            DiningPhilosopherState.Thinking => DiningThinkingBackground,
            DiningPhilosopherState.Hungry => DiningHungryBackground,
            DiningPhilosopherState.Eating => DiningEatingBackground,
            _ => DiningIdleBackground,
        };
    }

    private static Brush GetDiningAccent(DiningPhilosopherState state)
    {
        return state switch
        {
            DiningPhilosopherState.Thinking => DiningThinkingAccent,
            DiningPhilosopherState.Hungry => DiningHungryAccent,
            DiningPhilosopherState.Eating => DiningEatingAccent,
            _ => DiningIdleAccent,
        };
    }

    private void ApplyPreemptionSnapshot(PreemptionDemoSnapshot snapshot)
    {
        _isPreemptionTestRunning = snapshot.IsRunning;
        PreemptionTestState = snapshot.State;
        LastPreemptionEvent = snapshot.LastEvent;
        PreemptionHighLastInterval = snapshot.HighLastInterval;
        PreemptionLowWorkUnits = snapshot.LowWorkUnits;
        PreemptionHighRunCount = snapshot.HighRunCount;
        PreemptionYieldCount = snapshot.YieldCount;
        PreemptionLowLastThreadId = snapshot.LowLastThreadId;
        PreemptionHighLastThreadId = snapshot.HighLastThreadId;
        PreemptionCurrentExecutionThreadId = snapshot.CurrentExecutionThreadId;
        PreemptionActiveTask = snapshot.ActiveTask;
        PreemptionHealthLabel = snapshot.HealthLabel;
        PreemptionHealthDescription = snapshot.HealthDescription;
        PreemptionStarvationStatus = snapshot.StarvationStatus;
        PreemptionStarvationForeground = snapshot.IsStarvationWarning
            ? HealthWarningForeground
            : snapshot.IsRunning ? HealthGoodForeground : HealthStoppedForeground;
        PreemptionHealthBadgeBackground = snapshot.HealthLabel == "정상"
            ? HealthGoodBackground
            : snapshot.HealthLabel == "주의" ? HealthWarningBackground
            : snapshot.IsRunning ? HealthMonitoringBackground : HealthStoppedBackground;
        PreemptionHealthBadgeForeground = snapshot.HealthLabel == "정상"
            ? HealthGoodForeground
            : snapshot.HealthLabel == "주의" ? HealthWarningForeground
            : snapshot.IsRunning ? HealthMonitoringForeground : HealthStoppedForeground;
        PreemptionFlowHeadline = snapshot.FlowHeadline;
        PreemptionFlowDetail = snapshot.FlowDetail;
        PreemptionLowStepBrush = snapshot.LowStep ? FlowActiveBrush : FlowIdleBrush;
        PreemptionYieldStepBrush = snapshot.YieldStep ? FlowActiveBrush : FlowIdleBrush;
        PreemptionHighStepBrush = snapshot.HighStep ? FlowActiveBrush : FlowIdleBrush;
        UpdatePreemptionInspectorHighlight();

        SyncPreemptionLogs(snapshot.Logs);
        SyncPreemptionYieldTrend(snapshot.YieldTrend);

        PreemptionTrendMaxYield = snapshot.TrendMaxYield;
        SyncLowWorkerYieldViewModels(snapshot.LowWorkerYields);
    }

    private void SyncPreemptionLogs(IReadOnlyList<string> logs)
    {
        if (PreemptionLogs.Count == logs.Count)
        {
            var same = true;
            for (var i = 0; i < logs.Count; i++)
            {
                if (!string.Equals(PreemptionLogs[i], logs[i], StringComparison.Ordinal))
                {
                    same = false;
                    break;
                }
            }

            if (same)
            {
                return;
            }
        }

        PreemptionLogs.Clear();
        foreach (var log in logs)
        {
            PreemptionLogs.Add(log);
        }
    }

    private void SyncPreemptionYieldTrend(IReadOnlyList<PreemptionTrendPointSnapshot> points)
    {
        if (PreemptionYieldTrend.Count == points.Count)
        {
            var same = true;
            for (var i = 0; i < points.Count; i++)
            {
                if (!string.Equals(PreemptionYieldTrend[i].SecondLabel, points[i].SecondLabel, StringComparison.Ordinal)
                    || PreemptionYieldTrend[i].YieldCount != points[i].YieldCount)
                {
                    same = false;
                    break;
                }
            }

            if (same)
            {
                return;
            }
        }

        PreemptionYieldTrend.Clear();
        foreach (var point in points)
        {
            PreemptionYieldTrend.Add(new PreemptionTrendPointViewModel(point.SecondLabel, point.YieldCount));
        }
    }

    private void SyncLowWorkerYieldViewModels(IReadOnlyList<LowWorkerYieldSnapshot> snapshots)
    {
        if (PreemptionLowWorkerYields.Count == snapshots.Count)
        {
            var same = true;
            for (var i = 0; i < snapshots.Count; i++)
            {
                var viewModel = PreemptionLowWorkerYields[i];
                var snapshot = snapshots[i];
                if (!string.Equals(viewModel.Name, snapshot.Name, StringComparison.Ordinal)
                    || viewModel.YieldCount != snapshot.YieldCount
                    || !string.Equals(viewModel.LastResumeDelay, snapshot.LastResumeDelay, StringComparison.Ordinal))
                {
                    same = false;
                    break;
                }
            }

            if (same)
            {
                return;
            }
        }

        PreemptionLowWorkerYields.Clear();
        _lowWorkerYieldViewModels.Clear();

        foreach (var snapshot in snapshots)
        {
            var viewModel = new LowWorkerYieldStatsViewModel(snapshot.Name);
            viewModel.Update(snapshot.YieldCount, snapshot.LastResumeDelay);
            _lowWorkerYieldViewModels[snapshot.Name] = viewModel;
            PreemptionLowWorkerYields.Add(viewModel);
        }
    }

    private static string StripTimestamp(string log)
    {
        var closeIndex = log.IndexOf(']');
        return closeIndex >= 0 && closeIndex + 2 < log.Length
            ? log[(closeIndex + 2)..]
            : log;
    }

    private async Task StartPreemptionTestAsync()
    {
        if (_isPreemptionTestRunning)
        {
            return;
        }

        _preemptionDemo.Start();
        _isPreemptionTestRunning = true;

        await _dispatcher.InvokeAsync(() =>
        {
            ApplyPreemptionSnapshot(_preemptionDemo.CurrentSnapshot);
            _preemptionUiTimer.Start();
            RefreshCommandStates();
        });
    }

    private async Task StopPreemptionTestAsync()
    {
        var stopped = await _preemptionDemo.StopAsync().ConfigureAwait(false);
        if (!stopped)
        {
            await _dispatcher.InvokeAsync(() =>
            {
                ApplyPreemptionSnapshot(_preemptionDemo.CurrentSnapshot);
                RefreshCommandStates();
            });
            return;
        }

        _isPreemptionTestRunning = false;

        await _dispatcher.InvokeAsync(() =>
        {
            _preemptionUiTimer.Stop();
            ApplyPreemptionSnapshot(_preemptionDemo.CurrentSnapshot);
            ClearPreemptionActiveTasks();
            RefreshCommandStates();
        });
    }

    private void OnPreemptionUiTimerTick(object? sender, EventArgs e)
    {
        ApplyPreemptionSnapshot(_preemptionDemo.CurrentSnapshot);
    }

    private void UpdatePreemptionInspectorHighlight()
    {
        var isLowActive = PreemptionActiveTask.StartsWith("LOW", StringComparison.OrdinalIgnoreCase);
        var isHighActive = PreemptionActiveTask.StartsWith("HIGH", StringComparison.OrdinalIgnoreCase);
        var isRunningState = !string.Equals(PreemptionActiveTask, "대기 중", StringComparison.Ordinal) &&
                             !string.Equals(PreemptionActiveTask, "중지", StringComparison.Ordinal);

        PreemptionLowInspectorBackground = isLowActive ? InspectorLowActiveBackground : InspectorIdleBackground;
        PreemptionLowInspectorBorder = isLowActive ? InspectorLowActiveBorder : InspectorIdleBorder;

        PreemptionHighInspectorBackground = isHighActive ? InspectorHighActiveBackground : InspectorIdleBackground;
        PreemptionHighInspectorBorder = isHighActive ? InspectorHighActiveBorder : InspectorIdleBorder;

        PreemptionCurrentInspectorBackground = isRunningState ? InspectorCurrentActiveBackground : InspectorIdleBackground;
        PreemptionCurrentInspectorBorder = isRunningState ? InspectorCurrentActiveBorder : InspectorIdleBorder;
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

    private void OnPreemptionSchedulerSnapshotChanged(object? sender, SchedulerSnapshot snapshot)
    {
        lock (_preemptionSnapshotSyncRoot)
        {
            _latestPreemptionSnapshot = snapshot;
        }

        _dispatcher.BeginInvoke(ApplyLatestPreemptionSnapshot, DispatcherPriority.Background);
    }

    private void ApplyLatestPreemptionSnapshot()
    {
        SchedulerSnapshot? snapshot;

        lock (_preemptionSnapshotSyncRoot)
        {
            snapshot = _latestPreemptionSnapshot;
            _latestPreemptionSnapshot = null;
        }

        if (snapshot is null)
        {
            return;
        }

        foreach (var task in snapshot.Tasks)
        {
            if (_preemptionTaskViewModels.TryGetValue(task.Name, out var taskViewModel))
            {
                taskViewModel.Update(task);
                continue;
            }

            _preemptionTaskViewModels[task.Name] = new ScheduledTaskStatusViewModel(task);
        }

        SyncActiveTasks(snapshot, _preemptionActiveTaskNames);
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

        ApplyLatestSnapshot(snapshot);
    }

    private void ApplyLatestSnapshot(SchedulerSnapshot snapshot)
    {

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

        SyncActiveTasks(snapshot, _mainActiveTaskNames);
        RefreshCommandStates();
    }

    private void SyncActiveTasks(
        SchedulerSnapshot snapshot,
        HashSet<string> activeTaskNames)
    {
        var updatedActiveTaskNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var task in snapshot.Tasks)
        {
            if (task.IsEnabled)
            {
                updatedActiveTaskNames.Add(task.Name);
            }
        }

        if (activeTaskNames.SetEquals(updatedActiveTaskNames))
        {
            return;
        }

        activeTaskNames.Clear();
        activeTaskNames.UnionWith(updatedActiveTaskNames);

        RebuildActiveTasks();
    }

    private void ClearPreemptionActiveTasks()
    {
        lock (_preemptionSnapshotSyncRoot)
        {
            _latestPreemptionSnapshot = null;
        }

        _preemptionActiveTaskNames.Clear();
        _preemptionTaskViewModels.Clear();

        if (_dispatcher.CheckAccess())
        {
            RebuildActiveTasks();
            return;
        }

        _dispatcher.InvokeAsync(RebuildActiveTasks);
    }

    private void ClearDiningActiveTasks()
    {
        _diningActiveTaskNames.Clear();
        _diningTaskViewModels.Clear();

        if (_dispatcher.CheckAccess())
        {
            RebuildActiveTasks();
            return;
        }

        _dispatcher.InvokeAsync(RebuildActiveTasks);
    }

    private void RebuildActiveTasks()
    {
        ActiveTasks.Clear();
        AddActiveTasks(_mainActiveTaskNames, _taskViewModels);
        AddActiveTasks(_preemptionActiveTaskNames, _preemptionTaskViewModels);
        AddActiveTasks(_diningActiveTaskNames, _diningTaskViewModels);
    }

    private void AddActiveTasks(
        IEnumerable<string> activeTaskNames,
        IReadOnlyDictionary<string, ScheduledTaskStatusViewModel> taskViewModels)
    {
        foreach (var taskName in activeTaskNames)
        {
            if (taskViewModels.TryGetValue(taskName, out var taskViewModel))
            {
                ActiveTasks.Add(taskViewModel);
            }
        }
    }

    private void RefreshCommandStates()
    {
        StartCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        RunTestsCommand.RaiseCanExecuteChanged();
        StartPreemptionTestCommand.RaiseCanExecuteChanged();
        StopPreemptionTestCommand.RaiseCanExecuteChanged();
        StartDiningCommand.RaiseCanExecuteChanged();
        StopDiningCommand.RaiseCanExecuteChanged();
        ResetDiningCommand.RaiseCanExecuteChanged();
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

    internal sealed class LowWorkerYieldStatsViewModel : INotifyPropertyChanged
    {
        private int _yieldCount;
        private string _lastResumeDelay = "-";

        public LowWorkerYieldStatsViewModel(string name)
        {
            Name = name;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Name { get; }

        public int YieldCount
        {
            get => _yieldCount;
            private set => SetProperty(ref _yieldCount, value, nameof(YieldCount));
        }

        public string LastResumeDelay
        {
            get => _lastResumeDelay;
            private set => SetProperty(ref _lastResumeDelay, value, nameof(LastResumeDelay));
        }

        public void Update(int yieldCount, long lastResumeDelayTicks)
        {
            YieldCount = yieldCount;
            LastResumeDelay = lastResumeDelayTicks <= 0
                ? "-"
                : $"{TimeSpan.FromTicks(lastResumeDelayTicks).TotalMilliseconds:N0} ms";
        }

        public void Update(int yieldCount, string lastResumeDelay)
        {
            YieldCount = yieldCount;
            LastResumeDelay = lastResumeDelay;
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

    internal sealed class DiningPhilosopherViewModel : INotifyPropertyChanged
    {
        private string _state = "대기";
        private string _detail = "시작 대기";
        private int _mealCount;
        private int _waitCount;
        private double _activityProgress;
        private string _leftForkState = "왼쪽 포크 없음";
        private string _rightForkState = "오른쪽 포크 없음";
        private Brush _cardBackground = DiningIdleBackground;
        private Brush _accentBrush = DiningIdleAccent;

        public DiningPhilosopherViewModel(int index, string name)
        {
            Index = index;
            Name = name;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public int Index { get; }

        public string Name { get; }

        public string State
        {
            get => _state;
            private set => SetProperty(ref _state, value, nameof(State));
        }

        public string Detail
        {
            get => _detail;
            private set => SetProperty(ref _detail, value, nameof(Detail));
        }

        public int MealCount
        {
            get => _mealCount;
            private set => SetProperty(ref _mealCount, value, nameof(MealCount));
        }

        public int WaitCount
        {
            get => _waitCount;
            private set => SetProperty(ref _waitCount, value, nameof(WaitCount));
        }

        public double ActivityProgress
        {
            get => _activityProgress;
            private set => SetProperty(ref _activityProgress, value, nameof(ActivityProgress));
        }

        public string LeftForkState
        {
            get => _leftForkState;
            private set => SetProperty(ref _leftForkState, value, nameof(LeftForkState));
        }

        public string RightForkState
        {
            get => _rightForkState;
            private set => SetProperty(ref _rightForkState, value, nameof(RightForkState));
        }

        public Brush CardBackground
        {
            get => _cardBackground;
            private set => SetProperty(ref _cardBackground, value, nameof(CardBackground));
        }

        public Brush AccentBrush
        {
            get => _accentBrush;
            private set => SetProperty(ref _accentBrush, value, nameof(AccentBrush));
        }

        public void Update(DiningPhilosopherSnapshot snapshot, Brush background, Brush accent)
        {
            State = snapshot.StateText;
            Detail = snapshot.Detail;
            MealCount = snapshot.MealCount;
            WaitCount = snapshot.WaitCount;
            ActivityProgress = snapshot.ActivityProgress;
            LeftForkState = snapshot.LeftForkState;
            RightForkState = snapshot.RightForkState;
            CardBackground = background;
            AccentBrush = accent;
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

    internal sealed class DiningForkViewModel : INotifyPropertyChanged
    {
        private string _state = "비어 있음";
        private string _holder = "사용 가능";
        private Brush _background = DiningForkAvailableBrush;
        private Brush _accentBrush = DiningEatingAccent;

        public DiningForkViewModel(int index, string name)
        {
            Index = index;
            Name = name;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public int Index { get; }

        public string Name { get; }

        public string State
        {
            get => _state;
            private set => SetProperty(ref _state, value, nameof(State));
        }

        public string Holder
        {
            get => _holder;
            private set => SetProperty(ref _holder, value, nameof(Holder));
        }

        public Brush Background
        {
            get => _background;
            private set => SetProperty(ref _background, value, nameof(Background));
        }

        public Brush AccentBrush
        {
            get => _accentBrush;
            private set => SetProperty(ref _accentBrush, value, nameof(AccentBrush));
        }

        public void SetHeld(string holder)
        {
            State = "사용 중";
            Holder = holder;
            Background = DiningForkHeldBrush;
            AccentBrush = DiningHungryAccent;
        }

        public void SetAvailable()
        {
            State = "비어 있음";
            Holder = "사용 가능";
            Background = DiningForkAvailableBrush;
            AccentBrush = DiningEatingAccent;
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
}
