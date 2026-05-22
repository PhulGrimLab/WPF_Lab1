# Scheduling Progress

## Goal

현재 WPF 샘플 프로젝트에서 RTOS처럼 태스크를 주기적으로 실행하고, WPF 커스텀 컨트롤 예제들을 여러 개 동작시킬 수 있는 샘플 프로그램의 기본 스케줄링 구조를 만든다.

초기 목표는 실제 RTOS를 완전히 재현하는 것이 아니라, WPF 애플리케이션에서 안전하게 사용할 수 있는 RTOS 스타일의 태스크 스케줄링 골격을 만드는 것이다.

## Recommended Project Structure

```text
Wpf.Lib.Common
 └─ Scheduling
    ├─ SchedulerService.cs
    ├─ ScheduledTask.cs
    ├─ SchedulerContext.cs
    ├─ TaskPriority.cs
    ├─ TaskExecutionMode.cs
    ├─ SchedulerSnapshot.cs
    └─ IScheduledTask.cs

WpfSamples
 ├─ MainWindow.xaml
 ├─ MainWindowViewModel.cs
 └─ Samples
    ├─ ClockTask.cs
    ├─ CounterTask.cs
    └─ UiRefreshTask.cs
```

## Design Direction

`Wpf.Lib.Common` 프로젝트는 스케줄러 코어를 담는 공용 라이브러리로 사용한다.

`WpfSamples` 프로젝트는 스케줄러를 실제로 사용해서 태스크 상태를 화면에 보여주고, 이후 커스텀 컨트롤 샘플들을 연결하는 WPF 샘플 앱으로 사용한다.

중요한 원칙은 WPF UI 스레드와 스케줄러 실행 루프를 분리하는 것이다. 스케줄러는 백그라운드에서 태스크를 실행하고, UI 갱신은 반드시 WPF `Dispatcher`를 통해 처리한다.

## Initial Core Types

```csharp
public enum TaskPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}
```

```csharp
public enum TaskExecutionMode
{
    Periodic,
    OneShot
}
```

```csharp
public interface IScheduledTask
{
    string Name { get; }
    TaskPriority Priority { get; }
    TimeSpan Period { get; }
    TaskExecutionMode Mode { get; }

    DateTimeOffset NextRunAt { get; set; }
    bool IsEnabled { get; }

    Task ExecuteAsync(SchedulerContext context, CancellationToken cancellationToken);
}
```

```csharp
public sealed class SchedulerContext
{
    public SchedulerContext(DateTimeOffset now)
    {
        Now = now;
    }

    public DateTimeOffset Now { get; }
}
```

## Scheduler Service Concept

```csharp
public sealed class SchedulerService
{
    private readonly List<IScheduledTask> _tasks = [];
    private CancellationTokenSource? _cts;

    public event EventHandler<SchedulerSnapshot>? SnapshotChanged;

    public void Register(IScheduledTask task)
    {
        _tasks.Add(task);
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _ = RunAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.Now;

            var runnableTasks = _tasks
                .Where(x => x.IsEnabled && x.NextRunAt <= now)
                .OrderByDescending(x => x.Priority)
                .ThenBy(x => x.NextRunAt)
                .ToList();

            foreach (var task in runnableTasks)
            {
                await task.ExecuteAsync(new SchedulerContext(now), cancellationToken);

                if (task.Mode == TaskExecutionMode.Periodic)
                {
                    task.NextRunAt = now + task.Period;
                }
            }

            SnapshotChanged?.Invoke(this, SchedulerSnapshot.From(_tasks));

            await Task.Delay(10, cancellationToken);
        }
    }
}
```

## Sample Task Concept

```csharp
public sealed class CounterTask : IScheduledTask
{
    public string Name => "Counter";
    public TaskPriority Priority => TaskPriority.Normal;
    public TimeSpan Period => TimeSpan.FromMilliseconds(100);
    public TaskExecutionMode Mode => TaskExecutionMode.Periodic;
    public DateTimeOffset NextRunAt { get; set; } = DateTimeOffset.Now;
    public bool IsEnabled => true;

    public int Count { get; private set; }

    public Task ExecuteAsync(SchedulerContext context, CancellationToken cancellationToken)
    {
        Count++;
        return Task.CompletedTask;
    }
}
```

## WPF Binding Direction

스케줄러의 상태 변화 이벤트를 ViewModel에서 구독하고, UI 갱신은 `Application.Current.Dispatcher.BeginInvoke`로 처리한다.

```csharp
_scheduler.SnapshotChanged += (_, snapshot) =>
{
    Application.Current.Dispatcher.BeginInvoke(() =>
    {
        Tasks.Clear();

        foreach (var task in snapshot.Tasks)
        {
            Tasks.Add(task);
        }
    });
};
```

## First Implementation Scope

초기 버전에서는 아래 범위까지만 구현한다.

1. 태스크 등록
2. 주기 실행
3. 우선순위 정렬
4. 시작/정지
5. UI 표시용 스냅샷
6. WPF Dispatcher 분리

## Later Expansion Ideas

이후 단계에서 아래 기능을 확장한다.

1. 태스크 실행 시간 측정
2. Deadline miss 감지
3. 태스크 enable/disable
4. Pause/Resume
5. Fixed-rate와 fixed-delay 선택
6. UI Thread Task와 Worker Thread Task 구분
7. 로그/트레이스 뷰어
8. 커스텀 컨트롤 샘플별 태스크 연결

## Next Step

다음 작업에서는 `Wpf.Lib.Common/Scheduling` 폴더를 만들고, 위 구조를 실제 C# 파일로 구현한다.

그 다음 `WpfSamples/MainWindowViewModel.cs`에서 샘플 태스크 2~3개를 등록해서 작은 RTOS 모니터 화면처럼 표시한다.
