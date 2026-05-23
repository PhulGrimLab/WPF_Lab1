using System.Collections.Concurrent;
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

    private readonly SchedulerService _scheduler = new();
    private SchedulerService? _preemptionTestScheduler;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _preemptionUiTimer;
    private readonly ConcurrentQueue<string> _pendingPreemptionLogs = new();
    private readonly ConcurrentDictionary<string, LowWorkerRuntimeStats> _lowWorkerRuntimeStats = new();
    private readonly object _snapshotSyncRoot = new();
    private readonly object _preemptionSnapshotSyncRoot = new();
    private readonly Dictionary<string, ScheduledTaskStatusViewModel> _taskViewModels = [];
    private readonly Dictionary<string, ScheduledTaskStatusViewModel> _preemptionTaskViewModels = [];
    private readonly Dictionary<string, LowWorkerYieldStatsViewModel> _lowWorkerYieldViewModels = [];
    private readonly HashSet<string> _mainActiveTaskNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _preemptionActiveTaskNames = new(StringComparer.Ordinal);
    private SchedulerSnapshot? _latestSnapshot;
    private SchedulerSnapshot? _latestPreemptionSnapshot;
    private bool _isSnapshotApplyQueued;
    private bool _isRunningTests;
    private bool _isPreemptionTestRunning;
    private string _schedulerState = "Stopped";
    private string _lastSnapshotAt = "-";
    private string _testSummary = "Not run";
    private string _preemptionTestState = "Stopped";
    private string _lastPreemptionEvent = "-";
    private string _preemptionHighLastInterval = "-";
    private int _preemptionLowWorkUnits;
    private int _preemptionHighRunCount;
    private int _preemptionYieldCount;
    private int _preemptionLowLastThreadId;
    private int _preemptionHighLastThreadId;
    private int _preemptionCurrentExecutionThreadId;
    private int _preemptionLowWorkUnitsCounter;
    private int _preemptionHighRunCountCounter;
    private int _preemptionYieldCountCounter;
    private int _preemptionLowLastThreadIdCounter;
    private int _preemptionHighLastThreadIdCounter;
    private int _preemptionNormalRunCountCounter;
    private int _preemptionNormalLastThreadIdCounter;
    private DateTime _lastTrendSecond = DateTime.MinValue;
    private int _lastYieldTotalForTrend;
    private int _preemptionTrendMaxYield = 1;
    private string _preemptionHealthLabel = "중지";
    private string _preemptionHealthDescription = "데모를 시작하면 선점 상태를 분석합니다.";
    private Brush _preemptionHealthBadgeBackground = HealthStoppedBackground;
    private Brush _preemptionHealthBadgeForeground = HealthStoppedForeground;
    private string _preemptionFlowHeadline = "데모를 시작하면 선점 흐름을 단계별로 표시합니다.";
    private string _preemptionFlowDetail = "LOW 작업, HIGH 실행 가능, LOW 양보, HIGH 선실행 순서를 자동으로 추적합니다.";
    private Brush _preemptionLowStepBrush = FlowIdleBrush;
    private Brush _preemptionYieldStepBrush = FlowIdleBrush;
    private Brush _preemptionHighStepBrush = FlowIdleBrush;

    private long _lastLowWorkTick;
    private long _lastYieldTick;
    private long _lastHighRunTick;
    private long _lastNormalRunTick;
    private long _lastHighStartTick;
    private long _preemptionHighLastIntervalTicks;

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
            Interval = TimeSpan.FromMilliseconds(30),
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

        // Samples_RTOS의 Task 파일 등록
        RegisterSampleTasks();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ScheduledTaskStatusViewModel> Tasks { get; } = [];
    public ObservableCollection<ScheduledTaskStatusViewModel> ActiveTasks { get; } = [];
    public ObservableCollection<RtosTestResult> TestResults { get; } = [];
    public ObservableCollection<string> PreemptionLogs { get; } = [];
    public ObservableCollection<PreemptionTrendPointViewModel> PreemptionYieldTrend { get; } = [];
    public ObservableCollection<LowWorkerYieldStatsViewModel> PreemptionLowWorkerYields { get; } = [];

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
        _scheduler.SnapshotChanged -= OnSchedulerSnapshotChanged;
        _preemptionUiTimer.Stop();
        _preemptionUiTimer.Tick -= OnPreemptionUiTimerTick;
        await StopPreemptionTestAsync().ConfigureAwait(false);
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
        Interlocked.Exchange(ref _preemptionLowLastThreadIdCounter, 0);
        Interlocked.Exchange(ref _preemptionHighLastThreadIdCounter, 0);
        Interlocked.Exchange(ref _preemptionNormalRunCountCounter, 0);
        Interlocked.Exchange(ref _preemptionNormalLastThreadIdCounter, 0);
        Interlocked.Exchange(ref _lastLowWorkTick, 0);
        Interlocked.Exchange(ref _lastYieldTick, 0);
        Interlocked.Exchange(ref _lastHighRunTick, 0);
        Interlocked.Exchange(ref _lastNormalRunTick, 0);
        Interlocked.Exchange(ref _lastHighStartTick, 0);
        Interlocked.Exchange(ref _preemptionHighLastIntervalTicks, 0);
        ResetLowWorkerRuntimeStats();
        _lastTrendSecond = DateTime.MinValue;
        _lastYieldTotalForTrend = 0;
        ClearPendingPreemptionLogs();

        await _dispatcher.InvokeAsync(() =>
        {
            PreemptionLogs.Clear();
            PreemptionYieldTrend.Clear();
            PreemptionTestState = "Running";
            LastPreemptionEvent = "데모 시작: Low 3개, Normal 1개, High 1개 구성으로 선점 흐름을 관찰합니다.";
            PreemptionLowWorkUnits = 0;
            PreemptionHighRunCount = 0;
            PreemptionYieldCount = 0;
            PreemptionHighLastInterval = "-";
            PreemptionLowLastThreadId = 0;
            PreemptionHighLastThreadId = 0;
            PreemptionCurrentExecutionThreadId = 0;
            PreemptionActiveTask = "대기 중";
            InitializeLowWorkerYieldViewModels();
            UpdatePreemptionInspectorHighlight();
            PreemptionTrendMaxYield = 1;
            SetPreemptionHealth(
                label: "초기화",
                description: "데모를 시작했고, 첫 실행 데이터를 수집 중입니다.",
                background: HealthMonitoringBackground,
                foreground: HealthMonitoringForeground);
            SetPreemptionFlow(
                headline: "초기화 중",
                detail: "첫 실행 이벤트를 기다리는 중입니다.",
                lowStep: false,
                yieldStep: false,
                highStep: false);
            _preemptionUiTimer.Start();
            RefreshCommandStates();
        });

        var demoScheduler = new SchedulerService(
            tickInterval: TimeSpan.FromMilliseconds(5),
            snapshotInterval: TimeSpan.FromMilliseconds(50),
            timeQuantum: TimeSpan.FromMilliseconds(4));

        demoScheduler.SchedulerError += (_, ex) =>
        {
            QueuePreemptionLog($"Scheduler error: {ex.Message}");
        };
        demoScheduler.SnapshotChanged += OnPreemptionSchedulerSnapshotChanged;

        RegisterLowWorker(demoScheduler, "Low Worker A", TimeSpan.FromMilliseconds(25), 95, 2);
        RegisterLowWorker(demoScheduler, "Low Worker B", TimeSpan.FromMilliseconds(35), 120, 2);
        RegisterLowWorker(demoScheduler, "Low Worker C", TimeSpan.FromMilliseconds(45), 150, 1);

        demoScheduler.Register(new ScheduledTask(
            "Normal Telemetry",
            Enum_TaskPriority.Normal,
            TimeSpan.FromMilliseconds(110),
            Enum_TaskExecutionMode.Periodic,
            async (_, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Interlocked.Exchange(ref _preemptionNormalLastThreadIdCounter, Environment.CurrentManagedThreadId);
                var count = Interlocked.Increment(ref _preemptionNormalRunCountCounter);
                Interlocked.Exchange(ref _lastNormalRunTick, DateTime.UtcNow.Ticks);
                QueuePreemptionLog($"Normal Telemetry(TID={Environment.CurrentManagedThreadId})가 Low 작업 사이에 끼어들었습니다. 누적 실행 횟수={count}");
                await Task.Delay(8, cancellationToken).ConfigureAwait(false);
            },
            statusProvider: () => "Medium priority telemetry pulse"));

        demoScheduler.Register(new ScheduledTask(
            "High Priority Urgent",
            Enum_TaskPriority.Critical,
            TimeSpan.FromMilliseconds(180),
            Enum_TaskExecutionMode.Periodic,
            async (_, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var startedTicks = DateTime.UtcNow.Ticks;
                var previousStartedTicks = Interlocked.Exchange(ref _lastHighStartTick, startedTicks);
                if (previousStartedTicks > 0)
                {
                    Interlocked.Exchange(ref _preemptionHighLastIntervalTicks, startedTicks - previousStartedTicks);
                }

                Interlocked.Exchange(ref _preemptionHighLastThreadIdCounter, Environment.CurrentManagedThreadId);
                var count = Interlocked.Increment(ref _preemptionHighRunCountCounter);
                Interlocked.Exchange(ref _lastHighRunTick, DateTime.UtcNow.Ticks);
                var intervalText = previousStartedTicks > 0
                    ? $", 이전 실행 후 {TimeSpan.FromTicks(startedTicks - previousStartedTicks).TotalMilliseconds:N0}ms"
                    : string.Empty;
                QueuePreemptionLog($"High Priority Urgent(TID={Environment.CurrentManagedThreadId})가 긴급 구간을 시작했습니다. 누적 실행 횟수={count}{intervalText}");

                for (var burst = 1; burst <= 3; burst++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Delay(4, cancellationToken).ConfigureAwait(false);
                    QueuePreemptionLog($"High Priority Urgent burst {burst}/3 완료");
                }
            },
            statusProvider: () => "Critical burst work"));

        _preemptionTestScheduler = demoScheduler;
        demoScheduler.Start();
    }

    private void RegisterLowWorker(
        SchedulerService scheduler,
        string name,
        TimeSpan period,
        int workUnits,
        int delayMilliseconds)
    {
        scheduler.Register(new ScheduledTask(
            name,
            Enum_TaskPriority.Low,
            period,
            Enum_TaskExecutionMode.Periodic,
            async (context, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var stats = _lowWorkerRuntimeStats.GetOrAdd(name, _ => new LowWorkerRuntimeStats());
                var startUnit = Math.Clamp(Volatile.Read(ref stats.NextWorkUnit), 0, workUnits);

                RecordLowWorkerResume(name);
                Interlocked.Exchange(ref _preemptionLowLastThreadIdCounter, Environment.CurrentManagedThreadId);
                Interlocked.Exchange(ref _lastLowWorkTick, DateTime.UtcNow.Ticks);

                for (var i = startUnit; i < workUnits; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (context.ShouldYield())
                    {
                        Interlocked.Exchange(ref stats.NextWorkUnit, i);
                        context.MarkPreempted();
                        Interlocked.Increment(ref _preemptionYieldCountCounter);
                        Interlocked.Exchange(ref _lastYieldTick, DateTime.UtcNow.Ticks);
                        RecordLowWorkerYield(name);
                        QueuePreemptionLog($"{name}(TID={Environment.CurrentManagedThreadId})가 더 높은 우선순위 task를 감지해 양보했습니다. 진행률={i + 1}/{workUnits}");
                        return;
                    }

                    Interlocked.Increment(ref _preemptionLowWorkUnitsCounter);
                    await Task.Delay(delayMilliseconds, cancellationToken).ConfigureAwait(false);
                    Interlocked.Exchange(ref stats.NextWorkUnit, i + 1);
                }

                QueuePreemptionLog($"{name}가 긴 low-priority 작업 슬라이스 1회를 완료했습니다. workUnits={workUnits}");
                Interlocked.Exchange(ref stats.NextWorkUnit, 0);
            },
            statusProvider: () =>
            {
                var stats = _lowWorkerRuntimeStats.GetOrAdd(name, _ => new LowWorkerRuntimeStats());
                var nextUnit = Math.Clamp(Volatile.Read(ref stats.NextWorkUnit), 0, workUnits);
                return $"{nextUnit}/{workUnits} units / {period.TotalMilliseconds:N0}ms period";
            },
            overrunPolicy: Enum_TaskOverrunPolicy.FixedDelay));
    }

    private void ResetLowWorkerRuntimeStats()
    {
        _lowWorkerRuntimeStats.Clear();
        foreach (var name in GetLowWorkerNames())
        {
            _lowWorkerRuntimeStats[name] = new LowWorkerRuntimeStats();
        }
    }

    private void InitializeLowWorkerYieldViewModels()
    {
        _lowWorkerYieldViewModels.Clear();
        PreemptionLowWorkerYields.Clear();

        foreach (var name in GetLowWorkerNames())
        {
            var viewModel = new LowWorkerYieldStatsViewModel(name);
            _lowWorkerYieldViewModels[name] = viewModel;
            PreemptionLowWorkerYields.Add(viewModel);
        }
    }

    private void RecordLowWorkerYield(string name)
    {
        var stats = _lowWorkerRuntimeStats.GetOrAdd(name, _ => new LowWorkerRuntimeStats());
        Interlocked.Increment(ref stats.YieldCount);
        Interlocked.Exchange(ref stats.LastYieldUtcTicks, DateTime.UtcNow.Ticks);
    }

    private void RecordLowWorkerResume(string name)
    {
        var stats = _lowWorkerRuntimeStats.GetOrAdd(name, _ => new LowWorkerRuntimeStats());
        var yieldedAtTicks = Interlocked.Exchange(ref stats.LastYieldUtcTicks, 0);
        if (yieldedAtTicks <= 0)
        {
            return;
        }

        var resumedAtTicks = DateTime.UtcNow.Ticks;
        Interlocked.Exchange(ref stats.LastResumeDelayTicks, Math.Max(0, resumedAtTicks - yieldedAtTicks));
        Interlocked.Exchange(ref stats.LastResumeUtcTicks, resumedAtTicks);
    }

    private void UpdateLowWorkerYieldStats()
    {
        foreach (var name in GetLowWorkerNames())
        {
            if (!_lowWorkerYieldViewModels.TryGetValue(name, out var viewModel))
            {
                continue;
            }

            var stats = _lowWorkerRuntimeStats.GetOrAdd(name, _ => new LowWorkerRuntimeStats());
            viewModel.Update(
                Volatile.Read(ref stats.YieldCount),
                Volatile.Read(ref stats.LastResumeDelayTicks));
        }
    }

    private void UpdateHighInterval()
    {
        var intervalTicks = Volatile.Read(ref _preemptionHighLastIntervalTicks);
        PreemptionHighLastInterval = intervalTicks <= 0
            ? "-"
            : $"{TimeSpan.FromTicks(intervalTicks).TotalMilliseconds:N0} ms";
    }

    private static IEnumerable<string> GetLowWorkerNames()
    {
        yield return "Low Worker A";
        yield return "Low Worker B";
        yield return "Low Worker C";
    }

    private async Task StopPreemptionTestAsync()
    {
        var demoScheduler = _preemptionTestScheduler;
        _preemptionTestScheduler = null;

        if (demoScheduler is not null)
        {
            try
            {
                demoScheduler.SnapshotChanged -= OnPreemptionSchedulerSnapshotChanged;
                await demoScheduler.StopAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            finally
            {
                demoScheduler.Dispose();
            }

            ClearPreemptionActiveTasks();
        }

        if (_isPreemptionTestRunning)
        {
            _isPreemptionTestRunning = false;

            if (_dispatcher.CheckAccess())
            {
                _preemptionUiTimer.Stop();
                DrainPreemptionLogs();
                PreemptionTestState = "Stopped";
                LastPreemptionEvent = "Preemption demo stopped.";
                PreemptionCurrentExecutionThreadId = 0;
                PreemptionActiveTask = "중지";
                UpdatePreemptionInspectorHighlight();
                SetPreemptionHealth(
                    label: "중지",
                    description: "데모가 중지되어 상태 분석을 멈췄습니다.",
                    background: HealthStoppedBackground,
                    foreground: HealthStoppedForeground);
                SetPreemptionFlow(
                    headline: "중지",
                    detail: "데모가 멈춰 선점 흐름 추적도 중지되었습니다.",
                    lowStep: false,
                    yieldStep: false,
                    highStep: false);
                RefreshCommandStates();
            }
            else
            {
                await _dispatcher.InvokeAsync(() =>
                {
                    _preemptionUiTimer.Stop();
                    DrainPreemptionLogs();
                    PreemptionTestState = "Stopped";
                    LastPreemptionEvent = "Preemption demo stopped.";
                    PreemptionCurrentExecutionThreadId = 0;
                    PreemptionActiveTask = "중지";
                    UpdatePreemptionInspectorHighlight();
                    SetPreemptionHealth(
                        label: "중지",
                        description: "데모가 중지되어 상태 분석을 멈췄습니다.",
                        background: HealthStoppedBackground,
                        foreground: HealthStoppedForeground);
                    SetPreemptionFlow(
                        headline: "중지",
                        detail: "데모가 멈춰 선점 흐름 추적도 중지되었습니다.",
                        lowStep: false,
                        yieldStep: false,
                        highStep: false);
                    RefreshCommandStates();
                });
            }
        }
    }

    private void OnPreemptionUiTimerTick(object? sender, EventArgs e)
    {
        DrainPreemptionLogs();
        ApplyLatestPreemptionSnapshot();
        PreemptionLowWorkUnits = Volatile.Read(ref _preemptionLowWorkUnitsCounter);
        PreemptionHighRunCount = Volatile.Read(ref _preemptionHighRunCountCounter);
        PreemptionYieldCount = Volatile.Read(ref _preemptionYieldCountCounter);
        PreemptionLowLastThreadId = Volatile.Read(ref _preemptionLowLastThreadIdCounter);
        PreemptionHighLastThreadId = Volatile.Read(ref _preemptionHighLastThreadIdCounter);
        UpdateLowWorkerYieldStats();
        UpdateHighInterval();
        UpdatePreemptionHealthStatus();
        UpdatePreemptionFlowStatus();

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
        PreemptionLogs.Insert(0, line);

        while (PreemptionLogs.Count > 100)
        {
            PreemptionLogs.RemoveAt(PreemptionLogs.Count - 1);
        }
    }

    private void QueuePreemptionLog(string message)
    {
        _pendingPreemptionLogs.Enqueue(message);
    }

    private void ClearPendingPreemptionLogs()
    {
        while (_pendingPreemptionLogs.TryDequeue(out _))
        {
        }
    }

    private void DrainPreemptionLogs()
    {
        while (_pendingPreemptionLogs.TryDequeue(out var message))
        {
            AppendPreemptionLog(message);
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

    private void UpdatePreemptionFlowStatus()
    {
        if (!_isPreemptionTestRunning)
        {
            PreemptionCurrentExecutionThreadId = 0;
            PreemptionActiveTask = "중지";
            UpdatePreemptionInspectorHighlight();
            SetPreemptionFlow(
                headline: "중지",
                detail: "데모 시작 후 단계 신호가 켜집니다.",
                lowStep: false,
                yieldStep: false,
                highStep: false);
            return;
        }

        var nowTicks = DateTime.UtcNow.Ticks;
        var lowRecent = IsRecentTick(nowTicks, Volatile.Read(ref _lastLowWorkTick), TimeSpan.FromMilliseconds(900));
        var yieldRecent = IsRecentTick(nowTicks, Volatile.Read(ref _lastYieldTick), TimeSpan.FromMilliseconds(900));
        var normalRecent = IsRecentTick(nowTicks, Volatile.Read(ref _lastNormalRunTick), TimeSpan.FromMilliseconds(900));
        var highRecent = IsRecentTick(nowTicks, Volatile.Read(ref _lastHighRunTick), TimeSpan.FromMilliseconds(900));

        if (yieldRecent && highRecent)
        {
            PreemptionCurrentExecutionThreadId = PreemptionHighLastThreadId;
            PreemptionActiveTask = "HIGH Priority Urgent (선점 직후)";
            UpdatePreemptionInspectorHighlight();
            SetPreemptionFlow(
                headline: "선점 성립",
                detail: "LOW가 양보했고 HIGH가 즉시 선실행되었습니다.",
                lowStep: true,
                yieldStep: true,
                highStep: true);
            return;
        }

        if (highRecent)
        {
            PreemptionCurrentExecutionThreadId = PreemptionHighLastThreadId;
            PreemptionActiveTask = "HIGH Priority Urgent";
            UpdatePreemptionInspectorHighlight();
            SetPreemptionFlow(
                headline: "HIGH 우선 실행 구간",
                detail: "현재 HIGH 태스크가 CPU를 우선 사용 중입니다.",
                lowStep: false,
                yieldStep: false,
                highStep: true);
            return;
        }

        if (normalRecent)
        {
            PreemptionCurrentExecutionThreadId = Volatile.Read(ref _preemptionNormalLastThreadIdCounter);
            PreemptionActiveTask = "NORMAL Telemetry";
            UpdatePreemptionInspectorHighlight();
            SetPreemptionFlow(
                headline: "NORMAL 중간 우선순위 실행",
                detail: "LOW 작업들이 Ready 상태여도 NORMAL Telemetry가 먼저 실행되며 우선순위 차이를 보여줍니다.",
                lowStep: false,
                yieldStep: true,
                highStep: false);
            return;
        }

        if (lowRecent)
        {
            PreemptionCurrentExecutionThreadId = PreemptionLowLastThreadId;
            PreemptionActiveTask = "LOW Priority Worker";
            UpdatePreemptionInspectorHighlight();
            SetPreemptionFlow(
                headline: "LOW 작업 진행 중",
                detail: "LOW가 작업 중이며 HIGH runnable 신호를 대기하고 있습니다.",
                lowStep: true,
                yieldStep: false,
                highStep: false);
            return;
        }

    PreemptionCurrentExecutionThreadId = 0;
        PreemptionActiveTask = "대기 중";
        UpdatePreemptionInspectorHighlight();
        SetPreemptionFlow(
            headline: "다음 주기 대기",
            detail: "현재는 다음 주기 실행을 기다리는 구간입니다.",
            lowStep: false,
            yieldStep: false,
            highStep: false);
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

    private void SetPreemptionFlow(string headline, string detail, bool lowStep, bool yieldStep, bool highStep)
    {
        PreemptionFlowHeadline = headline;
        PreemptionFlowDetail = detail;
        PreemptionLowStepBrush = lowStep ? FlowActiveBrush : FlowIdleBrush;
        PreemptionYieldStepBrush = yieldStep ? FlowActiveBrush : FlowIdleBrush;
        PreemptionHighStepBrush = highStep ? FlowActiveBrush : FlowIdleBrush;
    }

    private static bool IsRecentTick(long nowTicks, long targetTicks, TimeSpan threshold)
    {
        if (targetTicks <= 0)
        {
            return false;
        }

        var elapsedTicks = nowTicks - targetTicks;
        return elapsedTicks >= 0 && elapsedTicks <= threshold.Ticks;
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
        activeTaskNames.Clear();

        foreach (var task in snapshot.Tasks)
        {
            if (task.IsEnabled)
            {
                activeTaskNames.Add(task.Name);
            }
        }

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

    private void RebuildActiveTasks()
    {
        ActiveTasks.Clear();
        AddActiveTasks(_mainActiveTaskNames, _taskViewModels);
        AddActiveTasks(_preemptionActiveTaskNames, _preemptionTaskViewModels);
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

    private sealed class LowWorkerRuntimeStats
    {
        public int YieldCount;
        public int NextWorkUnit;
        public long LastYieldUtcTicks;
        public long LastResumeDelayTicks;
        public long LastResumeUtcTicks;
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
