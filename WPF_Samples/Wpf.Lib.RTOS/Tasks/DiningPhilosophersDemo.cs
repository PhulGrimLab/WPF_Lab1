using System.Diagnostics;

namespace Wpf.Lib.RTOS;

/// <summary>
/// RTOS scheduler and semaphore based Dining Philosophers demo.
/// </summary>
public sealed class DiningPhilosophersDemo : IDisposable, IAsyncDisposable
{
    private const int PhilosopherCount = 5;
    private const int RoomCapacity = PhilosopherCount - 1;
    private readonly object _syncRoot = new();
    private readonly SchedulerService _scheduler;
    private readonly PhilosopherRuntime[] _philosophers;
    private readonly ForkRuntime[] _forks;
    private readonly List<string> _logs = [];
    private RtosSemaphore[] _forkSemaphores;
    private RtosSemaphore _roomSemaphore;
    private DateTimeOffset _startedAt = DateTimeOffset.MinValue;
    private bool _isRunning;
    private bool _isDisposed;

    public DiningPhilosophersDemo(TimeSpan? tickInterval = null, TimeSpan? snapshotInterval = null)
    {
        _scheduler = new SchedulerService(
            tickInterval ?? TimeSpan.FromMilliseconds(20),
            snapshotInterval ?? TimeSpan.FromMilliseconds(100));
        _scheduler.SnapshotChanged += OnSchedulerSnapshotChanged;

        _forkSemaphores = CreateForkSemaphores();
        _roomSemaphore = new RtosSemaphore(initialCount: RoomCapacity, maxCount: RoomCapacity);
        _forks = Enumerable.Range(0, PhilosopherCount)
            .Select(index => new ForkRuntime(index, $"포크 {index + 1}"))
            .ToArray();
        _philosophers = Enumerable.Range(0, PhilosopherCount)
            .Select(index => new PhilosopherRuntime(index, $"철학자 {index + 1}"))
            .ToArray();

        foreach (var philosopher in _philosophers)
        {
            philosopher.Reset(DateTimeOffset.Now);
            _scheduler.Register(new PhilosopherTask(this, philosopher));
        }
    }

    public event EventHandler<DiningPhilosophersSnapshot>? SnapshotChanged;

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

    public DiningPhilosophersSnapshot CurrentSnapshot
    {
        get
        {
            lock (_syncRoot)
            {
                return CreateSnapshotCore();
            }
        }
    }

    public void Start()
    {
        ThrowIfDisposed();

        lock (_syncRoot)
        {
            if (_isRunning)
            {
                return;
            }

            ResetCore(DateTimeOffset.Now);
            _isRunning = true;
            _startedAt = DateTimeOffset.Now;
            AddLogCore("철학자의 식사 데모를 시작했습니다.");
        }

        _scheduler.Start();
        PublishSnapshot();
    }

    public async Task<bool> StopAsync()
    {
        if (_isDisposed)
        {
            return true;
        }

        var stopped = await _scheduler.StopAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        if (!stopped)
        {
            lock (_syncRoot)
            {
                AddLogCore("철학자의 식사 데모 중지 요청이 시간 내 완료되지 않았습니다.");
            }

            PublishSnapshot();
            return false;
        }

        lock (_syncRoot)
        {
            if (!_isRunning)
            {
                return true;
            }

            _isRunning = false;
            ReleaseHeldResourcesCore();
            AddLogCore("철학자의 식사 데모를 중지했습니다.");
        }

        PublishSnapshot();
        return true;
    }

    public void Reset()
    {
        ThrowIfDisposed();

        lock (_syncRoot)
        {
            ResetCore(DateTimeOffset.Now);
            AddLogCore("철학자의 식사 데모를 초기화했습니다.");
        }

        PublishSnapshot();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _scheduler.SnapshotChanged -= OnSchedulerSnapshotChanged;
        _scheduler.Dispose();
        DisposeSemaphores();
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _scheduler.SnapshotChanged -= OnSchedulerSnapshotChanged;
        await _scheduler.DisposeAsync().ConfigureAwait(false);
        DisposeSemaphores();
    }

    private void OnSchedulerSnapshotChanged(object? sender, SchedulerSnapshot snapshot)
    {
        RaiseSchedulerSnapshotChanged(snapshot);
        RaiseSnapshotChanged(CurrentSnapshot);
    }

    private async Task ExecutePhilosopherAsync(PhilosopherRuntime philosopher, SchedulerContext context, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.Now;

        lock (_syncRoot)
        {
            if (!_isRunning)
            {
                philosopher.SetIdle();
                return;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        switch (philosopher.State)
        {
            case DiningPhilosopherState.Thinking:
                ExecuteThinking(philosopher, now);
                break;
            case DiningPhilosopherState.Hungry:
                await ExecuteHungryAsync(philosopher, cancellationToken).ConfigureAwait(false);
                break;
            case DiningPhilosopherState.Eating:
                ExecuteEating(philosopher, now);
                break;
            default:
                lock (_syncRoot)
                {
                    philosopher.BeginThinking(now, Random.Shared.Next(700, 1500));
                }
                break;
        }
    }

    private void ExecuteThinking(PhilosopherRuntime philosopher, DateTimeOffset now)
    {
        lock (_syncRoot)
        {
            if (now < philosopher.PhaseEndsAt)
            {
                philosopher.UpdateProgress(now);
                return;
            }

            philosopher.BeginHungry();
        }
    }

    private async Task ExecuteHungryAsync(PhilosopherRuntime philosopher, CancellationToken cancellationToken)
    {
        if (!philosopher.RoomEntered)
        {
            if (!await _roomSemaphore.WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false))
            {
                lock (_syncRoot)
                {
                    philosopher.Detail = "waiter 세마포어 대기";
                }

                return;
            }

            lock (_syncRoot)
            {
                philosopher.RoomEntered = true;
                philosopher.Detail = "식탁 입장";
            }
        }

        var leftForkIndex = philosopher.Index;
        var rightForkIndex = (philosopher.Index + 1) % PhilosopherCount;

        if (!philosopher.LeftForkHeld)
        {
            if (!await _forkSemaphores[leftForkIndex].WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false))
            {
                lock (_syncRoot)
                {
                    philosopher.Detail = $"왼쪽 포크 {leftForkIndex + 1} 대기";
                }

                return;
            }

            lock (_syncRoot)
            {
                philosopher.LeftForkHeld = true;
                philosopher.LeftForkState = $"포크 {leftForkIndex + 1} 확보";
                _forks[leftForkIndex].SetHeld(philosopher.Name);
            }
        }

        if (!philosopher.RightForkHeld)
        {
            if (!await _forkSemaphores[rightForkIndex].WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false))
            {
                lock (_syncRoot)
                {
                    philosopher.LeftForkHeld = false;
                    philosopher.RoomEntered = false;
                    philosopher.LeftForkState = "왼쪽 포크 없음";
                    _forks[leftForkIndex].SetAvailable();
                    _forkSemaphores[leftForkIndex].Release();
                    _roomSemaphore.Release();
                    philosopher.Detail = $"오른쪽 포크 {rightForkIndex + 1} 대기";
                }

                return;
            }

            lock (_syncRoot)
            {
                philosopher.RightForkHeld = true;
                philosopher.RightForkState = $"포크 {rightForkIndex + 1} 확보";
                _forks[rightForkIndex].SetHeld(philosopher.Name);
                philosopher.BeginEating(DateTimeOffset.Now, Random.Shared.Next(550, 1100));
                AddLogCore($"{philosopher.Name} 식사 시작");
            }
        }
    }

    private void ExecuteEating(PhilosopherRuntime philosopher, DateTimeOffset now)
    {
        lock (_syncRoot)
        {
            if (now < philosopher.PhaseEndsAt)
            {
                philosopher.UpdateProgress(now);
                return;
            }

            philosopher.MealCount++;
            AddLogCore($"{philosopher.Name} 식사 완료");
        }

        ReleasePhilosopherResources(philosopher);

        lock (_syncRoot)
        {
            philosopher.BeginThinking(DateTimeOffset.Now, Random.Shared.Next(700, 1500));
        }
    }

    private void ReleasePhilosopherResources(PhilosopherRuntime philosopher)
    {
        var leftForkIndex = philosopher.Index;
        var rightForkIndex = (philosopher.Index + 1) % PhilosopherCount;
        var releaseLeft = false;
        var releaseRight = false;
        var releaseRoom = false;

        lock (_syncRoot)
        {
            releaseRight = philosopher.RightForkHeld;
            releaseLeft = philosopher.LeftForkHeld;
            releaseRoom = philosopher.RoomEntered;

            if (releaseRight)
            {
                philosopher.RightForkHeld = false;
                philosopher.RightForkState = "오른쪽 포크 없음";
                _forks[rightForkIndex].SetAvailable();
            }

            if (releaseLeft)
            {
                philosopher.LeftForkHeld = false;
                philosopher.LeftForkState = "왼쪽 포크 없음";
                _forks[leftForkIndex].SetAvailable();
            }

            if (releaseRoom)
            {
                philosopher.RoomEntered = false;
            }
        }

        if (releaseRight)
        {
            _forkSemaphores[rightForkIndex].Release();
        }

        if (releaseLeft)
        {
            _forkSemaphores[leftForkIndex].Release();
        }

        if (releaseRoom)
        {
            _roomSemaphore.Release();
        }
    }

    private void ResetCore(DateTimeOffset now)
    {
        ReleaseHeldResourcesCore();
        DisposeSemaphores();
        _forkSemaphores = CreateForkSemaphores();
        _roomSemaphore = new RtosSemaphore(initialCount: RoomCapacity, maxCount: RoomCapacity);
        _startedAt = _isRunning ? now : DateTimeOffset.MinValue;
        _logs.Clear();

        foreach (var fork in _forks)
        {
            fork.SetAvailable();
        }

        foreach (var philosopher in _philosophers)
        {
            philosopher.Reset(now);
        }
    }

    private void ReleaseHeldResourcesCore()
    {
        foreach (var philosopher in _philosophers)
        {
            if (philosopher.RightForkHeld)
            {
                _forkSemaphores[(philosopher.Index + 1) % PhilosopherCount].Release();
            }

            if (philosopher.LeftForkHeld)
            {
                _forkSemaphores[philosopher.Index].Release();
            }

            if (philosopher.RoomEntered)
            {
                _roomSemaphore.Release();
            }

            philosopher.ClearHeldResources();
        }

        foreach (var fork in _forks)
        {
            fork.SetAvailable();
        }
    }

    private DiningPhilosophersSnapshot CreateSnapshotCore()
    {
        return new DiningPhilosophersSnapshot(
            DateTimeOffset.Now,
            _isRunning,
            _startedAt,
            _philosophers.Select(philosopher => philosopher.ToSnapshot()).ToArray(),
            _forks.Select(fork => fork.ToSnapshot()).ToArray(),
            _logs.ToArray());
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

    private void RaiseSnapshotChanged(DiningPhilosophersSnapshot snapshot)
    {
        var handlers = SnapshotChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler<DiningPhilosophersSnapshot> handler in handlers.GetInvocationList())
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

    private void AddLogCore(string message)
    {
        _logs.Insert(0, $"[{DateTime.Now:HH:mm:ss.fff}] {message}");
        if (_logs.Count > 80)
        {
            _logs.RemoveRange(80, _logs.Count - 80);
        }
    }

    private void DisposeSemaphores()
    {
        foreach (var fork in _forkSemaphores)
        {
            fork.Dispose();
        }

        _roomSemaphore.Dispose();
    }

    private static RtosSemaphore[] CreateForkSemaphores()
    {
        return Enumerable.Range(0, PhilosopherCount)
            .Select(_ => new RtosSemaphore(initialCount: 1, maxCount: 1))
            .ToArray();
    }

    private static TimeSpan GetPhilosopherTaskPeriod(int index)
    {
        // 동일 주기로 인한 고정 순서를 줄여 식사 시작 시점이 자연스럽게 분산되도록 한다.
        var milliseconds = 45 + (index * 7);
        return TimeSpan.FromMilliseconds(milliseconds);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }

    private sealed class PhilosopherTask : SchedulerTaskBase
    {
        private readonly DiningPhilosophersDemo _demo;
        private readonly PhilosopherRuntime _philosopher;

        public PhilosopherTask(DiningPhilosophersDemo demo, PhilosopherRuntime philosopher)
            : base(
                $"Dining {philosopher.Name}",
                Enum_TaskPriority.Normal,
                GetPhilosopherTaskPeriod(philosopher.Index),
                Enum_TaskExecutionMode.Periodic,
                Enum_TaskOverrunPolicy.FixedDelay)
        {
            _demo = demo;
            _philosopher = philosopher;
        }

        public override string Status => $"{_philosopher.StateText} / {_philosopher.Detail}";

        public override Task ExecuteAsync(SchedulerContext context, CancellationToken cancellationToken)
        {
            return _demo.ExecutePhilosopherAsync(_philosopher, context, cancellationToken);
        }
    }

    private sealed class PhilosopherRuntime
    {
        public PhilosopherRuntime(int index, string name)
        {
            Index = index;
            Name = name;
        }

        public int Index { get; }
        public string Name { get; }
        public DiningPhilosopherState State { get; private set; } = DiningPhilosopherState.Idle;
        public string StateText { get; private set; } = "대기";
        public string Detail { get; set; } = "시작 대기";
        public int MealCount { get; set; }
        public int WaitCount { get; private set; }
        public double ActivityProgress { get; private set; }
        public DateTimeOffset PhaseStartedAt { get; private set; }
        public DateTimeOffset PhaseEndsAt { get; private set; }
        public bool RoomEntered { get; set; }
        public bool LeftForkHeld { get; set; }
        public bool RightForkHeld { get; set; }
        public string LeftForkState { get; set; } = "왼쪽 포크 없음";
        public string RightForkState { get; set; } = "오른쪽 포크 없음";

        public void Reset(DateTimeOffset now)
        {
            MealCount = 0;
            WaitCount = 0;
            ClearHeldResources();
            BeginThinking(now, Random.Shared.Next(700, 1500));
        }

        public void SetIdle()
        {
            State = DiningPhilosopherState.Idle;
            StateText = "대기";
            Detail = "시작 대기";
            ActivityProgress = 0;
        }

        public void BeginThinking(DateTimeOffset now, int durationMilliseconds)
        {
            State = DiningPhilosopherState.Thinking;
            StateText = "생각 중";
            Detail = "문제를 곱씹는 중";
            ActivityProgress = 0;
            PhaseStartedAt = now;
            PhaseEndsAt = now + TimeSpan.FromMilliseconds(durationMilliseconds);
        }

        public void BeginHungry()
        {
            State = DiningPhilosopherState.Hungry;
            StateText = "배고픔";
            Detail = "waiter 세마포어 대기";
            ActivityProgress = 0;
            WaitCount++;
        }

        public void BeginEating(DateTimeOffset now, int durationMilliseconds)
        {
            State = DiningPhilosopherState.Eating;
            StateText = "식사 중";
            Detail = "두 포크 확보";
            ActivityProgress = 0;
            PhaseStartedAt = now;
            PhaseEndsAt = now + TimeSpan.FromMilliseconds(durationMilliseconds);
        }

        public void UpdateProgress(DateTimeOffset now)
        {
            var totalTicks = Math.Max(1, (PhaseEndsAt - PhaseStartedAt).Ticks);
            var elapsedTicks = Math.Clamp((now - PhaseStartedAt).Ticks, 0, totalTicks);
            ActivityProgress = elapsedTicks * 100.0 / totalTicks;
        }

        public void ClearHeldResources()
        {
            RoomEntered = false;
            LeftForkHeld = false;
            RightForkHeld = false;
            LeftForkState = "왼쪽 포크 없음";
            RightForkState = "오른쪽 포크 없음";
        }

        public DiningPhilosopherSnapshot ToSnapshot()
        {
            return new DiningPhilosopherSnapshot(
                Index,
                Name,
                State,
                StateText,
                Detail,
                MealCount,
                WaitCount,
                ActivityProgress,
                LeftForkState,
                RightForkState);
        }
    }

    private sealed class ForkRuntime
    {
        public ForkRuntime(int index, string name)
        {
            Index = index;
            Name = name;
        }

        public int Index { get; }
        public string Name { get; }
        public bool IsHeld { get; private set; }
        public string Holder { get; private set; } = "사용 가능";

        public void SetHeld(string holder)
        {
            IsHeld = true;
            Holder = holder;
        }

        public void SetAvailable()
        {
            IsHeld = false;
            Holder = "사용 가능";
        }

        public DiningForkSnapshot ToSnapshot()
        {
            return new DiningForkSnapshot(
                Index,
                Name,
                IsHeld,
                IsHeld ? "사용 중" : "비어 있음",
                Holder);
        }
    }
}

public enum DiningPhilosopherState
{
    Idle,
    Thinking,
    Hungry,
    Eating
}

public sealed record DiningPhilosophersSnapshot(
    DateTimeOffset CreatedAt,
    bool IsRunning,
    DateTimeOffset StartedAt,
    IReadOnlyList<DiningPhilosopherSnapshot> Philosophers,
    IReadOnlyList<DiningForkSnapshot> Forks,
    IReadOnlyList<string> Logs);

public sealed record DiningPhilosopherSnapshot(
    int Index,
    string Name,
    DiningPhilosopherState State,
    string StateText,
    string Detail,
    int MealCount,
    int WaitCount,
    double ActivityProgress,
    string LeftForkState,
    string RightForkState);

public sealed record DiningForkSnapshot(
    int Index,
    string Name,
    bool IsHeld,
    string State,
    string Holder);
