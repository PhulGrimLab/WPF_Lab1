using System.Diagnostics;
using System.Collections.Concurrent;

namespace Wpf.Lib.RTOS;

public sealed class PreemptionDemo : IDisposable, IAsyncDisposable
{
    private const int MaxLogs = 100;
    private readonly object _syncRoot = new();
    private readonly ConcurrentDictionary<string, LowWorkerRuntimeStats> _lowWorkerRuntimeStats = new();
    private readonly ConcurrentDictionary<string, int> _logOccurrenceCounts = new();
    private readonly List<string> _logs = [];
    private SchedulerService? _scheduler;
    private SchedulerSnapshot? _latestSchedulerSnapshot;
    private DateTime _lastTrendSecond = DateTime.MinValue;
    private int _lastYieldTotalForTrend;
    private readonly List<PreemptionTrendPointSnapshot> _yieldTrend = [];
    private bool _isRunning;
    private DateTimeOffset _sessionStartedAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastLowProgressAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastHighProgressAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastLowStartedAt = DateTimeOffset.MinValue;
    private long _lastObservedLowRunCount;
    private long _lastObservedHighRunCount;
    private bool _isStarvationDetected;
    private double _starvationLowStallSeconds = 3.0;
    private int _lowWorkUnits;
    private int _highRunCount;
    private int _yieldCount;
    private int _lowLastThreadId;
    private int _highLastThreadId;
    private int _normalRunCount;
    private int _normalLastThreadId;
    private long _lastLowWorkTick;
    private long _lastYieldTick;
    private long _lastHighRunTick;
    private long _lastNormalRunTick;
    private long _lastHighStartTick;
    private long _highLastIntervalTicks;
    private bool _isDisposed;

    public event EventHandler<PreemptionDemoSnapshot>? SnapshotChanged;

    public event EventHandler<SchedulerSnapshot>? SchedulerSnapshotChanged;

    public bool IsRunning
    {
        get
        {
            lock (_syncRoot)
            {
                return _isRunning;
            }
        }
    }

    public double StarvationLowStallSeconds
    {
        get
        {
            lock (_syncRoot)
            {
                return _starvationLowStallSeconds;
            }
        }
        set
        {
            lock (_syncRoot)
            {
                _starvationLowStallSeconds = Math.Clamp(value, 1.0, 8.0);
            }

            PublishSnapshot();
        }
    }

    public PreemptionDemoSnapshot CurrentSnapshot
    {
        get
        {
            lock (_syncRoot)
            {
                return CreateSnapshotCore(DateTimeOffset.Now);
            }
        }
    }

    public void Start()
    {
        ThrowIfDisposed();

        if (IsRunning)
        {
            return;
        }

        lock (_syncRoot)
        {
            ResetRuntimeCore();
            _isRunning = true;
            _sessionStartedAt = DateTimeOffset.UtcNow;
            _lastLowProgressAt = _sessionStartedAt;
            _lastHighProgressAt = _sessionStartedAt;
            AddLogCore("데모 시작: Low 3개, Normal 1개, High 1개 구성으로 선점 흐름을 관찰합니다.");
        }

        var scheduler = new SchedulerService(
            tickInterval: TimeSpan.FromMilliseconds(5),
            snapshotInterval: TimeSpan.FromMilliseconds(50),
            timeQuantum: TimeSpan.FromMilliseconds(15));

        scheduler.SchedulerError += (_, ex) => AddLog($"Scheduler error: {ex.Message}");
        scheduler.SnapshotChanged += OnSchedulerSnapshotChanged;

        RegisterLowWorker(scheduler, "Low Worker A", TimeSpan.FromMilliseconds(25), 95, 2);
        RegisterLowWorker(scheduler, "Low Worker B", TimeSpan.FromMilliseconds(35), 120, 2);
        RegisterLowWorker(scheduler, "Low Worker C", TimeSpan.FromMilliseconds(45), 150, 1);
        RegisterNormalTelemetry(scheduler);
        RegisterHighUrgent(scheduler);

        lock (_syncRoot)
        {
            _scheduler = scheduler;
        }

        scheduler.Start();
        PublishSnapshot();
    }

    public async Task<bool> StopAsync()
    {
        SchedulerService? scheduler;

        lock (_syncRoot)
        {
            scheduler = _scheduler;
            _scheduler = null;
        }

        if (scheduler is not null)
        {
            scheduler.SnapshotChanged -= OnSchedulerSnapshotChanged;
            var stopped = await scheduler.StopAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            if (!stopped)
            {
                AddLog("Preemption demo stop timeout. Scheduler is still stopping.");
                PublishSnapshot();
                return false;
            }

            scheduler.Dispose();
        }

        lock (_syncRoot)
        {
            if (!_isRunning)
            {
                return true;
            }

            _isRunning = false;
            AddLogCore("Preemption demo stopped.");
        }

        PublishSnapshot();
        return true;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _ = StopAsync().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _ = await StopAsync().ConfigureAwait(false);
    }

    private void RegisterNormalTelemetry(SchedulerService scheduler)
    {
        scheduler.Register(new ScheduledTask(
            "Normal Telemetry",
            Enum_TaskPriority.Normal,
            TimeSpan.FromMilliseconds(110),
            Enum_TaskExecutionMode.Periodic,
            async (_, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Interlocked.Exchange(ref _normalLastThreadId, Environment.CurrentManagedThreadId);
                var count = Interlocked.Increment(ref _normalRunCount);
                Interlocked.Exchange(ref _lastNormalRunTick, DateTime.UtcNow.Ticks);
                AddLogEvery("normal-telemetry", 5, () => $"Normal Telemetry가 누적 {count}회 실행되었습니다.");
                await Task.Delay(8, cancellationToken).ConfigureAwait(false);
            },
            statusProvider: () => "Medium priority telemetry pulse"));
    }

    private void RegisterHighUrgent(SchedulerService scheduler)
    {
        scheduler.Register(new ScheduledTask(
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
                    Interlocked.Exchange(ref _highLastIntervalTicks, startedTicks - previousStartedTicks);
                }

                Interlocked.Exchange(ref _highLastThreadId, Environment.CurrentManagedThreadId);
                var count = Interlocked.Increment(ref _highRunCount);
                Interlocked.Exchange(ref _lastHighRunTick, DateTime.UtcNow.Ticks);
                var intervalText = previousStartedTicks > 0
                    ? $", 이전 실행 후 {TimeSpan.FromTicks(startedTicks - previousStartedTicks).TotalMilliseconds:N0}ms"
                    : string.Empty;
                AddLogEvery("high-urgent-start", 3, () => $"High Priority Urgent가 누적 {count}회 시작되었습니다{intervalText}.");

                for (var burst = 1; burst <= 3; burst++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Delay(4, cancellationToken).ConfigureAwait(false);
                }

                AddLogEvery("high-urgent-burst", 3, () => $"High Priority Urgent burst 처리 누적 {count}회 완료");
            },
            statusProvider: () => "Critical burst work"));
    }

    private void RegisterLowWorker(SchedulerService scheduler, string name, TimeSpan period, int workUnits, int delayMilliseconds)
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
                Interlocked.Exchange(ref _lowLastThreadId, Environment.CurrentManagedThreadId);
                Interlocked.Exchange(ref _lastLowWorkTick, DateTime.UtcNow.Ticks);

                for (var i = startUnit; i < workUnits; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (context.ShouldYield())
                    {
                        Interlocked.Exchange(ref stats.NextWorkUnit, i);
                        context.MarkPreempted();
                        Interlocked.Increment(ref _yieldCount);
                        Interlocked.Exchange(ref _lastYieldTick, DateTime.UtcNow.Ticks);
                        RecordLowWorkerYield(name);
                        var totalYieldCount = Volatile.Read(ref _yieldCount);
                        AddLogEvery($"yield-{name}", 4, () => $"{name} 양보 누적 {totalYieldCount}회 (최근 진행률 {i + 1}/{workUnits})");
                        return;
                    }

                    Interlocked.Increment(ref _lowWorkUnits);
                    await Task.Delay(delayMilliseconds, cancellationToken).ConfigureAwait(false);
                    Interlocked.Exchange(ref stats.NextWorkUnit, i + 1);
                }

                AddLogEvery($"low-complete-{name}", 2, () => $"{name}가 low 작업 슬라이스를 누적 완료했습니다. workUnits={workUnits}");
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

    private void OnSchedulerSnapshotChanged(object? sender, SchedulerSnapshot snapshot)
    {
        lock (_syncRoot)
        {
            _latestSchedulerSnapshot = snapshot;
            UpdateStarvationSignalCore(snapshot);
            UpdateTrendCore(snapshot.CreatedAt.LocalDateTime);
        }

        RaiseSchedulerSnapshotChanged(snapshot);
        RaiseSnapshotChanged(CurrentSnapshot);
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

    private void UpdateStarvationSignalCore(SchedulerSnapshot snapshot)
    {
        if (!_isRunning)
        {
            _isStarvationDetected = false;
            return;
        }

        var lowTasks = snapshot.Tasks
            .Where(task => task.Name.StartsWith("Low Worker", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (lowTasks.Length == 0)
        {
            _isStarvationDetected = false;
            return;
        }

        var higherPriorityTasks = snapshot.Tasks
            .Where(task => task.Priority > Enum_TaskPriority.Low)
            .ToArray();
        var lowRunCount = lowTasks.Sum(task => task.RunCount);
        var higherRunCount = higherPriorityTasks.Sum(task => task.RunCount);
        var latestLowStartedAt = lowTasks
            .Where(task => task.LastStartedAt.HasValue)
            .Select(task => task.LastStartedAt!.Value)
            .DefaultIfEmpty(DateTimeOffset.MinValue)
            .Max();

        if (lowRunCount > _lastObservedLowRunCount)
        {
            _lastLowProgressAt = snapshot.CreatedAt;
            _lastObservedLowRunCount = lowRunCount;
        }

        if (higherRunCount > _lastObservedHighRunCount)
        {
            _lastHighProgressAt = snapshot.CreatedAt;
            _lastObservedHighRunCount = higherRunCount;
        }

        if (latestLowStartedAt != DateTimeOffset.MinValue)
        {
            _lastLowStartedAt = latestLowStartedAt;
        }

        var now = snapshot.CreatedAt;
        var lowStallThreshold = TimeSpan.FromSeconds(_starvationLowStallSeconds);
        var highRecentlyProgressed = _lastHighProgressAt != DateTimeOffset.MinValue
            && now - _lastHighProgressAt <= TimeSpan.FromSeconds(1.5);
        var lowProgressStalled = now - _lastLowProgressAt >= lowStallThreshold;
        var lowStartStalled = _lastLowStartedAt == DateTimeOffset.MinValue
            || now - _lastLowStartedAt >= lowStallThreshold;
        var readyOrRunningHigher = higherPriorityTasks.Any(task => task.State is Enum_TaskState.Ready or Enum_TaskState.Running);
        var enoughWarmupTime = now - _sessionStartedAt >= TimeSpan.FromSeconds(2);

        _isStarvationDetected = enoughWarmupTime
            && highRecentlyProgressed
            && readyOrRunningHigher
            && lowProgressStalled
            && lowStartStalled;
    }

    private void UpdateTrendCore(DateTime now)
    {
        var nowSecond = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, now.Kind);
        if (nowSecond == _lastTrendSecond)
        {
            return;
        }

        var currentYieldTotal = Volatile.Read(ref _yieldCount);
        var deltaYield = Math.Max(0, currentYieldTotal - _lastYieldTotalForTrend);
        _lastYieldTotalForTrend = currentYieldTotal;
        _lastTrendSecond = nowSecond;
        _yieldTrend.Add(new PreemptionTrendPointSnapshot(nowSecond.ToString("HH:mm:ss"), deltaYield));

        while (_yieldTrend.Count > 10)
        {
            _yieldTrend.RemoveAt(0);
        }
    }

    private PreemptionDemoSnapshot CreateSnapshotCore(DateTimeOffset now)
    {
        var highLastIntervalTicks = Volatile.Read(ref _highLastIntervalTicks);
        var lowWorkUnits = Volatile.Read(ref _lowWorkUnits);
        var highRunCount = Volatile.Read(ref _highRunCount);
        var yieldCount = Volatile.Read(ref _yieldCount);
        var active = GetActiveState();
        var health = GetHealth(lowWorkUnits, highRunCount, yieldCount);
        var lowWorkers = GetLowWorkerNames()
            .Select(name =>
            {
                var stats = _lowWorkerRuntimeStats.GetOrAdd(name, _ => new LowWorkerRuntimeStats());
                var resumeTicks = Volatile.Read(ref stats.LastResumeDelayTicks);
                return new LowWorkerYieldSnapshot(
                    name,
                    Volatile.Read(ref stats.YieldCount),
                    resumeTicks <= 0 ? "-" : $"{TimeSpan.FromTicks(resumeTicks).TotalMilliseconds:N0} ms");
            })
            .ToArray();

        return new PreemptionDemoSnapshot(
            now,
            _isRunning,
            _isRunning ? "Running" : "Stopped",
            _logs.ToArray(),
            _logs.Count > 0 ? StripLogTimestamp(_logs[0]) : "-",
            highLastIntervalTicks <= 0 ? "-" : $"{TimeSpan.FromTicks(highLastIntervalTicks).TotalMilliseconds:N0} ms",
            lowWorkUnits,
            highRunCount,
            yieldCount,
            Volatile.Read(ref _lowLastThreadId),
            Volatile.Read(ref _highLastThreadId),
            active.ActiveTask,
            active.CurrentExecutionThreadId,
            health.HealthLabel,
            health.HealthDescription,
            health.StarvationStatus,
            health.IsStarvationWarning,
            active.FlowHeadline,
            active.FlowDetail,
            active.LowStep,
            active.YieldStep,
            active.HighStep,
            _yieldTrend.ToArray(),
            lowWorkers,
            Math.Max(1, _yieldTrend.Count == 0 ? 1 : _yieldTrend.Max(item => item.YieldCount)),
            _starvationLowStallSeconds);
    }

    private PreemptionActiveState GetActiveState()
    {
        if (!_isRunning)
        {
            return new PreemptionActiveState("중지", 0, "중지", "데모 시작 후 단계 신호가 켜집니다.", false, false, false);
        }

        var nowTicks = DateTime.UtcNow.Ticks;
        var lowRecent = IsRecentTick(nowTicks, Volatile.Read(ref _lastLowWorkTick), TimeSpan.FromMilliseconds(900));
        var yieldRecent = IsRecentTick(nowTicks, Volatile.Read(ref _lastYieldTick), TimeSpan.FromMilliseconds(900));
        var normalRecent = IsRecentTick(nowTicks, Volatile.Read(ref _lastNormalRunTick), TimeSpan.FromMilliseconds(900));
        var highRecent = IsRecentTick(nowTicks, Volatile.Read(ref _lastHighRunTick), TimeSpan.FromMilliseconds(900));

        if (yieldRecent && highRecent)
        {
            return new PreemptionActiveState(
                "HIGH Priority Urgent (선점 직후)",
                Volatile.Read(ref _highLastThreadId),
                "선점 성립",
                "LOW가 양보했고 HIGH가 즉시 선실행되었습니다.",
                true,
                true,
                true);
        }

        if (highRecent)
        {
            return new PreemptionActiveState(
                "HIGH Priority Urgent",
                Volatile.Read(ref _highLastThreadId),
                "HIGH 우선 실행 구간",
                "현재 HIGH 태스크가 CPU를 우선 사용 중입니다.",
                false,
                false,
                true);
        }

        if (normalRecent)
        {
            return new PreemptionActiveState(
                "NORMAL Telemetry",
                Volatile.Read(ref _normalLastThreadId),
                "NORMAL 중간 우선순위 실행",
                "LOW 작업들이 Ready 상태여도 NORMAL Telemetry가 먼저 실행되며 우선순위 차이를 보여줍니다.",
                false,
                true,
                false);
        }

        if (lowRecent)
        {
            return new PreemptionActiveState(
                "LOW Priority Worker",
                Volatile.Read(ref _lowLastThreadId),
                "LOW 작업 진행 중",
                "LOW가 작업 중이며 HIGH runnable 신호를 대기하고 있습니다.",
                true,
                false,
                false);
        }

        return new PreemptionActiveState("대기 중", 0, "다음 주기 대기", "현재는 다음 주기 실행을 기다리는 구간입니다.", false, false, false);
    }

    private PreemptionHealthState GetHealth(int lowWorkUnits, int highRunCount, int yieldCount)
    {
        if (!_isRunning)
        {
            return new PreemptionHealthState("중지", "데모를 시작하면 선점 상태를 분석합니다.", "기아 감지: 중지", false);
        }

        if (_isStarvationDetected)
        {
            return new PreemptionHealthState(
                "주의",
                "상위 우선순위는 진행 중인데 LOW RunCount/LastStartedAt 갱신이 지연됩니다.",
                "기아 감지: 주의 (LOW 진행 정체)",
                true);
        }

        if (highRunCount < 2)
        {
            return new PreemptionHealthState("초기화", "아직 실행 횟수가 적어서 판단을 보류합니다.", "기아 감지: 정상", false);
        }

        if (yieldCount >= 3)
        {
            return new PreemptionHealthState("정상", "양보가 반복 발생하여 협력형 선점이 잘 작동 중입니다.", "기아 감지: 정상", false);
        }

        if (lowWorkUnits >= 120 && yieldCount == 0)
        {
            return new PreemptionHealthState("주의", "낮은 우선순위 작업량만 증가합니다. 양보 호출 지점을 점검하세요.", "기아 감지: 정상", true);
        }

        return new PreemptionHealthState("관찰", "실행 데이터가 누적되는 중입니다. 추세를 조금 더 관찰하세요.", "기아 감지: 정상", false);
    }

    private void AddLogEvery(string key, int every, Func<string> messageFactory)
    {
        if (every <= 1)
        {
            AddLog(messageFactory());
            return;
        }

        var count = _logOccurrenceCounts.AddOrUpdate(key, 1, static (_, current) => current + 1);
        if (count % every == 0)
        {
            AddLog(messageFactory());
        }
    }

    private void AddLog(string message)
    {
        lock (_syncRoot)
        {
            AddLogCore(message);
        }
    }

    private void AddLogCore(string message)
    {
        _logs.Insert(0, $"[{DateTime.Now:HH:mm:ss.fff}] {message}");
        if (_logs.Count > MaxLogs)
        {
            _logs.RemoveRange(MaxLogs, _logs.Count - MaxLogs);
        }
    }

    private void ResetRuntimeCore()
    {
        _lowWorkerRuntimeStats.Clear();
        foreach (var name in GetLowWorkerNames())
        {
            _lowWorkerRuntimeStats[name] = new LowWorkerRuntimeStats();
        }

        _logOccurrenceCounts.Clear();
        _logs.Clear();
        _yieldTrend.Clear();
        _latestSchedulerSnapshot = null;
        _lastTrendSecond = DateTime.MinValue;
        _lastYieldTotalForTrend = 0;
        _lastLowStartedAt = DateTimeOffset.MinValue;
        _lastObservedLowRunCount = 0;
        _lastObservedHighRunCount = 0;
        _isStarvationDetected = false;
        Interlocked.Exchange(ref _lowWorkUnits, 0);
        Interlocked.Exchange(ref _highRunCount, 0);
        Interlocked.Exchange(ref _yieldCount, 0);
        Interlocked.Exchange(ref _lowLastThreadId, 0);
        Interlocked.Exchange(ref _highLastThreadId, 0);
        Interlocked.Exchange(ref _normalRunCount, 0);
        Interlocked.Exchange(ref _normalLastThreadId, 0);
        Interlocked.Exchange(ref _lastLowWorkTick, 0);
        Interlocked.Exchange(ref _lastYieldTick, 0);
        Interlocked.Exchange(ref _lastHighRunTick, 0);
        Interlocked.Exchange(ref _lastNormalRunTick, 0);
        Interlocked.Exchange(ref _lastHighStartTick, 0);
        Interlocked.Exchange(ref _highLastIntervalTicks, 0);
    }

    private void PublishSnapshot()
    {
        RaiseSnapshotChanged(CurrentSnapshot);
    }

    private void RaiseSchedulerSnapshotChanged(SchedulerSnapshot snapshot)
    {
        var handlers = SchedulerSnapshotChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler<SchedulerSnapshot> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, snapshot);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }
    }

    private void RaiseSnapshotChanged(PreemptionDemoSnapshot snapshot)
    {
        var handlers = SnapshotChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler<PreemptionDemoSnapshot> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, snapshot);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }

    private static IEnumerable<string> GetLowWorkerNames()
    {
        yield return "Low Worker A";
        yield return "Low Worker B";
        yield return "Low Worker C";
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

    private static string StripLogTimestamp(string log)
    {
        var closeIndex = log.IndexOf(']');
        return closeIndex >= 0 && closeIndex + 2 < log.Length
            ? log[(closeIndex + 2)..]
            : log;
    }

    private sealed class LowWorkerRuntimeStats
    {
        public int YieldCount;
        public int NextWorkUnit;
        public long LastYieldUtcTicks;
        public long LastResumeDelayTicks;
        public long LastResumeUtcTicks;
    }

    private sealed record PreemptionActiveState(
        string ActiveTask,
        int CurrentExecutionThreadId,
        string FlowHeadline,
        string FlowDetail,
        bool LowStep,
        bool YieldStep,
        bool HighStep);

    private sealed record PreemptionHealthState(
        string HealthLabel,
        string HealthDescription,
        string StarvationStatus,
        bool IsStarvationWarning);
}

public sealed record PreemptionDemoSnapshot(
    DateTimeOffset CreatedAt,
    bool IsRunning,
    string State,
    IReadOnlyList<string> Logs,
    string LastEvent,
    string HighLastInterval,
    int LowWorkUnits,
    int HighRunCount,
    int YieldCount,
    int LowLastThreadId,
    int HighLastThreadId,
    string ActiveTask,
    int CurrentExecutionThreadId,
    string HealthLabel,
    string HealthDescription,
    string StarvationStatus,
    bool IsStarvationWarning,
    string FlowHeadline,
    string FlowDetail,
    bool LowStep,
    bool YieldStep,
    bool HighStep,
    IReadOnlyList<PreemptionTrendPointSnapshot> YieldTrend,
    IReadOnlyList<LowWorkerYieldSnapshot> LowWorkerYields,
    int TrendMaxYield,
    double StarvationLowStallSeconds);

public sealed record PreemptionTrendPointSnapshot(string SecondLabel, int YieldCount);

public sealed record LowWorkerYieldSnapshot(string Name, int YieldCount, string LastResumeDelay);
