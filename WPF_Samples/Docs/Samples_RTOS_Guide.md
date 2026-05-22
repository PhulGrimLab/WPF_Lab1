# WpfSamples Samples_RTOS 연동 설명

이 문서는 `WpfSamples/Samples_RTOS` 폴더의 샘플 태스크들이 `Wpf.Lib.RTOS` 라이브러리와 어떻게 연결되어 동작하는지 설명합니다.

## 샘플 폴더 구조

```text
WpfSamples
 └─ Samples_RTOS
    ├─ CounterTask.cs
    ├─ ClockTask.cs
    └─ UiRefreshTask.cs
```

이 세 파일은 모두 스케줄러에 등록되는 태스크입니다.

## 공통 구조

세 태스크는 모두 `IScheduledTask` 인터페이스를 구현합니다.

```csharp
internal sealed class CounterTask : IScheduledTask
```

이 말은 `CounterTask`가 스케줄러에 등록될 수 있는 모양을 갖췄다는 뜻입니다.

스케줄러는 각 태스크를 구체적인 클래스 이름으로 보지 않고 `IScheduledTask`로 봅니다.

```text
SchedulerService 입장:
    CounterTask도 IScheduledTask
    ClockTask도 IScheduledTask
    UiRefreshTask도 IScheduledTask
```

그래서 스케줄러는 모든 태스크를 같은 방식으로 다룰 수 있습니다.

## CounterTask.cs

`CounterTask`는 100ms마다 숫자를 증가시키는 태스크입니다.

중요 속성은 다음과 같습니다.

```csharp
public string Name => "Counter Task";
public Enum_TaskPriority Priority => Enum_TaskPriority.Normal;
public TimeSpan Period => TimeSpan.FromMilliseconds(100);
public Enum_TaskExecutionMode Mode => Enum_TaskExecutionMode.Periodic;
```

의미는 다음과 같습니다.

```text
이름: Counter Task
우선순위: Normal
주기: 100ms
실행 방식: 반복 실행
```

실제 실행 코드는 `ExecuteAsync`입니다.

```csharp
public Task ExecuteAsync(SchedulerContext context, CancellationToken cancellationToken)
{
    Count++;
    return Task.CompletedTask;
}
```

실행될 때마다 `Count`가 1씩 증가합니다.

화면에 표시되는 상태는 `Status`입니다.

```csharp
public string Status => $"Count = {Count:N0}";
```

DataGrid의 `Status` 컬럼에는 예를 들어 `Count = 152`처럼 표시됩니다.

## ClockTask.cs

`ClockTask`는 1초마다 현재 시간을 갱신하는 태스크입니다.

```csharp
public string Name => "Clock Task";
public Enum_TaskPriority Priority => Enum_TaskPriority.Low;
public TimeSpan Period => TimeSpan.FromSeconds(1);
public Enum_TaskExecutionMode Mode => Enum_TaskExecutionMode.Periodic;
```

의미는 다음과 같습니다.

```text
이름: Clock Task
우선순위: Low
주기: 1초
실행 방식: 반복 실행
```

실행 코드는 다음입니다.

```csharp
public Task ExecuteAsync(SchedulerContext context, CancellationToken cancellationToken)
{
    CurrentTimeText = context.Now.LocalDateTime.ToString("HH:mm:ss");
    return Task.CompletedTask;
}
```

여기서 `context.Now`는 스케줄러가 태스크를 실행할 때 넘겨준 기준 시각입니다.

화면에는 `Status`를 통해 현재 시간이 표시됩니다.

```csharp
public string Status => CurrentTimeText;
```

## UiRefreshTask.cs

`UiRefreshTask`는 250ms마다 실행되는 샘플 태스크입니다.

이름은 UI Refresh이지만, 실제 WPF UI를 직접 갱신하지는 않습니다.

중요한 이유가 있습니다.

WPF UI는 UI 스레드에서만 안전하게 갱신해야 합니다.

`UiRefreshTask`는 스케줄러가 백그라운드에서 실행하기 때문에 직접 화면을 만지면 안 됩니다.

이 태스크는 "조금 시간이 걸리는 백그라운드 작업"을 흉내냅니다.

```csharp
public async Task ExecuteAsync(SchedulerContext context, CancellationToken cancellationToken)
{
    RefreshTick++;
    await Task.Delay(15, cancellationToken).ConfigureAwait(false);
}
```

실행될 때마다 `RefreshTick`이 증가하고, 15ms 동안 대기합니다.

이 대기는 실제 작업 시간이 조금 걸리는 상황을 흉내내기 위한 것입니다.

## MainWindowViewModel에서 태스크 등록

샘플 태스크들은 `MainWindowViewModel`에서 등록됩니다.

```csharp
private void RegisterSampleTasks()
{
    _scheduler.Register(new CounterTask());
    _scheduler.Register(new ClockTask());
    _scheduler.Register(new UiRefreshTask());
}
```

이 코드는 다음 의미입니다.

```text
스케줄러야, CounterTask를 관리해줘.
스케줄러야, ClockTask를 관리해줘.
스케줄러야, UiRefreshTask를 관리해줘.
```

등록만 했다고 바로 실행되지는 않습니다.

실행은 Start 버튼을 눌러 `_scheduler.Start()`가 호출될 때 시작됩니다.

## Start 버튼을 눌렀을 때

`MainWindowViewModel`에는 Start 버튼과 연결된 명령이 있습니다.

```csharp
StartCommand = new RelayCommand(StartScheduler, () => !_scheduler.IsRunning);
```

Start 버튼을 누르면 `StartScheduler`가 실행됩니다.

```csharp
private void StartScheduler()
{
    _scheduler.Start();
    RefreshCommandStates();
}
```

이때부터 `SchedulerService`가 백그라운드에서 등록된 태스크들을 실행합니다.

## Stop 버튼을 눌렀을 때

Stop 버튼은 `StopSchedulerAsync`와 연결됩니다.

```csharp
StopCommand = new RelayCommand(
    () => _ = StopSchedulerAsync(),
    () => _scheduler.IsRunning);
```

Stop 버튼을 누르면 다음 코드가 실행됩니다.

```csharp
private async Task StopSchedulerAsync()
{
    await _scheduler.StopAsync().ConfigureAwait(false);
    await _dispatcher.InvokeAsync(RefreshCommandStates);
}
```

스케줄러는 취소 요청을 받고 루프를 멈춥니다.

## SnapshotChanged와 화면 갱신

`SchedulerService`는 일정 시간마다 `SnapshotChanged` 이벤트를 발생시킵니다.

`MainWindowViewModel`은 이 이벤트를 구독합니다.

```csharp
_scheduler.SnapshotChanged += OnSchedulerSnapshotChanged;
```

스냅샷이 오면 바로 DataGrid를 바꾸지 않습니다.

대신 WPF Dispatcher를 사용합니다.

```csharp
_dispatcher.BeginInvoke(ApplyLatestSnapshot, DispatcherPriority.Background);
```

이유는 스케줄러가 백그라운드 스레드에서 실행되고, WPF UI는 UI 스레드에서만 갱신해야 하기 때문입니다.

## ApplyLatestSnapshot

`ApplyLatestSnapshot`은 스냅샷 데이터를 화면용 ViewModel에 반영합니다.

```csharp
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
```

처음 보는 태스크이면 새 행을 만듭니다.

이미 있는 태스크이면 기존 행을 업데이트합니다.

이렇게 하면 매번 DataGrid 행을 지웠다가 다시 만들지 않아도 됩니다.

메모리 사용량과 UI 부하를 줄이는 데 도움이 됩니다.

## ScheduledTaskStatusViewModel 역할

`ScheduledTaskStatusViewModel`은 DataGrid 한 행에 해당합니다.

스냅샷의 데이터를 화면 표시용 문자열로 바꿔 저장합니다.

예를 들면 다음과 같습니다.

```csharp
Period = $"{snapshot.Period.TotalMilliseconds:N0} ms";
LastStartedAt = snapshot.LastStartedAt?.LocalDateTime.ToString("HH:mm:ss.fff") ?? "-";
LastDuration = snapshot.LastDuration is null
    ? "-"
    : $"{snapshot.LastDuration.Value.TotalMilliseconds:N2} ms";
```

즉 내부 데이터는 `TimeSpan`, `DateTimeOffset`이지만, 화면에는 보기 좋은 문자열로 표시됩니다.

## MainWindow.xaml의 DataGrid

화면에는 `DataGrid`가 있고, `Tasks` 컬렉션에 바인딩되어 있습니다.

```xml
<DataGrid ItemsSource="{Binding Tasks}">
```

각 컬럼은 `ScheduledTaskStatusViewModel`의 속성과 연결됩니다.

```xml
<DataGridTextColumn Header="Task"
                    Binding="{Binding Name}"/>

<DataGridTextColumn Header="Status"
                    Binding="{Binding Status}"/>

<DataGridTextColumn Header="Runs"
                    Binding="{Binding RunCount}"/>
```

스케줄러가 태스크를 실행합니다.

스냅샷이 만들어집니다.

ViewModel이 스냅샷을 화면용 값으로 변환합니다.

DataGrid가 그 값을 보여줍니다.

## 전체 연결 그림

```text
Start Button
    |
    v
MainWindowViewModel.StartScheduler()
    |
    v
SchedulerService.Start()
    |
    v
RunAsync loop
    |
    +-- CounterTask.ExecuteAsync()
    +-- ClockTask.ExecuteAsync()
    +-- UiRefreshTask.ExecuteAsync()
    |
    v
SchedulerSnapshot 생성
    |
    v
SnapshotChanged 이벤트
    |
    v
MainWindowViewModel.OnSchedulerSnapshotChanged()
    |
    v
Dispatcher.BeginInvoke()
    |
    v
ApplyLatestSnapshot()
    |
    v
DataGrid 갱신
```

## 새 샘플 태스크를 추가하는 방법

새 태스크를 추가하려면 다음 순서로 하면 됩니다.

1. `Samples_RTOS` 폴더에 새 클래스 파일을 만든다.
2. `IScheduledTask`를 구현하거나 `SchedulerTaskBase`를 상속한다.
3. `Name`, `Priority`, `Period`, `Mode`, `Status`, `ExecuteAsync`를 작성한다.
4. `MainWindowViewModel.RegisterSampleTasks`에서 `_scheduler.Register(new MyTask())`를 추가한다.

예시:

```csharp
internal sealed class MyTask : SchedulerTaskBase
{
    private int _value;

    public MyTask()
        : base(
            "My Task",
            Enum_TaskPriority.Normal,
            TimeSpan.FromSeconds(1),
            Enum_TaskExecutionMode.Periodic)
    {
    }

    public override string Status => $"Value = {_value}";

    public override Task ExecuteAsync(
        SchedulerContext context,
        CancellationToken cancellationToken)
    {
        _value++;
        return Task.CompletedTask;
    }
}
```

등록:

```csharp
_scheduler.Register(new MyTask());
```

## 주의할 점

태스크 안에서 WPF UI 컨트롤을 직접 수정하지 않는 것이 좋습니다.

예를 들어 태스크 안에서 `TextBlock.Text = "..."`처럼 직접 접근하면 문제가 생길 수 있습니다.

태스크는 백그라운드 작업만 수행하고, 화면 갱신은 `SnapshotChanged`와 ViewModel을 통해 처리하는 구조가 안전합니다.

---

## 2026-05-22 선점 데모 연동 보강

최근 샘플은 기본 3개 태스크 외에 "선점 데모"를 별도로 제공합니다.

### A. 선점 데모 구성

1. LOW Priority Worker
2. HIGH Priority Urgent

핵심 흐름:

1. LOW가 작업을 수행
2. HIGH가 runnable 되면 LOW가 `context.ShouldYield()`를 감지
3. LOW가 양보(return)
4. HIGH가 먼저 실행

### B. UI에서 확인하는 포인트

선점 데모 탭에서 아래를 함께 확인하면 이해가 가장 빠릅니다.

1. 현재 동작 태스크
2. LOW/HIGH 마지막 실행 TID
3. 양보 횟수 / HIGH 실행 횟수
4. 실시간 로그

### C. UI 스레드 해석 주의

현재 화면에는 "참고 UI TID"도 함께 보입니다.

주의:

1. UI TID는 선점 판정 대상이 아닙니다.
2. 선점 판정은 RTOS 태스크(LOW/HIGH) 사이에서만 수행됩니다.

### D. 초보자 실습 추천

1. 선점 데모 시작
2. LOW/HIGH 카드 색상 변화 관찰
3. 로그에서 `ShouldYield` 메시지 확인
4. 현재 실행 TID가 LOW/HIGH 중 어느 쪽으로 바뀌는지 비교

이 과정을 한 번 보면 협력형 선점 개념이 빠르게 잡힙니다.
