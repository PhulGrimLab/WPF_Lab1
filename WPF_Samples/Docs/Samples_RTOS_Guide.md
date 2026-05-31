# Wpf.Lib.RTOS 데모 조합 연동 설명

이 문서는 현재 구조 기준으로 RTOS Monitor 샘플 태스크가 어디에 있고,
WPF와 라이브러리가 어떻게 역할을 나누는지 설명합니다.

## 현재 구조 요약

기존 `WpfSamples/Samples_RTOS` 폴더 기반 구조는 제거되었고,
샘플 태스크와 모니터 조합은 라이브러리로 이동했습니다.

```text
Wpf.Lib.RTOS
 └─ Tasks
    ├─ SampleTasks.cs
    │  ├─ CounterTask
    │  ├─ ClockTask
    │  └─ UiRefreshTask
    └─ SchedulerMonitorDemo.cs

WpfSamples
 └─ MainWindowViewModel.cs
    ├─ SchedulerMonitorDemo 시작/중지 호출
    └─ SnapshotChanged 구독 후 스냅샷 상태 반영
```

## 모니터 샘플 태스크 정의 위치

샘플 태스크 정의는 `Wpf.Lib.RTOS/Tasks/SampleTasks.cs`에 있습니다.

1. `CounterTask`
- 100ms 주기로 카운터 증가

2. `ClockTask`
- 1초 주기로 현재 시각 갱신

3. `UiRefreshTask`
- 250ms 주기 샘플 작업(실제 WPF 컨트롤 직접 갱신 아님)

## 실행 주체

`Wpf.Lib.RTOS/Tasks/SchedulerMonitorDemo.cs`가 아래를 담당합니다.

1. `SchedulerService` 생성/소유
2. 샘플 태스크 등록
3. Start/Stop/Dispose 수명주기 관리
4. `SnapshotChanged` 이벤트 발행

즉 RTOS Monitor의 실행 엔진과 태스크 등록 책임은 라이브러리 쪽에 있습니다.

## WPF 역할

`WpfSamples/MainWindowViewModel.cs`는 아래 역할만 수행합니다.

1. Start/Stop 버튼에서 `SchedulerMonitorDemo` 호출
2. `SnapshotChanged`를 받아 화면용 ViewModel로 반영
3. Dispatcher를 통해 UI 스레드에서 안전하게 상태를 반영

핵심 원칙:

1. 라이브러리: 실행/상태 생성
2. WPF: 표시 계층/상호작용
3. 선점 데모는 `SchedulerService` snapshot과 `PreemptionDemo` snapshot을 분리해, UI는 상태 표시만 수행합니다.

## 최근 성능 정리

1. 선점 판정은 우선순위별 runnable 캐시를 먼저 확인한 뒤, 필요한 경우에만 상위 우선순위 태스크를 조회합니다.
2. EventFlags의 `WaitAny`는 비트별 인덱스를 사용해 대기자 탐색 범위를 줄입니다.
3. 스냅샷 발행은 상태 변화가 없으면 건너뛰도록 조정되어 있습니다.
4. 선점 데모 로그는 배치 반영과 지연 문자열 생성으로 UI 할당 비용을 줄였습니다.

## 스냅샷 상태 반영 흐름

```text
SchedulerMonitorDemo.Start()
  -> 내부 SchedulerService 실행
  -> SnapshotChanged(snapshot) 발행
  -> MainWindowViewModel 수신
  -> ApplyLatestSnapshot(snapshot)
  -> DataGrid/ActiveTasks 갱신
```

이 구조 덕분에 UI는 태스크 내부 상태를 직접 만지지 않고,
읽기 전용 snapshot만 사용해 안전하게 표시할 수 있습니다.

## 새 모니터 샘플 태스크 추가 방법

새 태스크를 추가하려면 다음 순서로 하면 됩니다.

1. `Wpf.Lib.RTOS/Tasks/SampleTasks.cs`에 새 태스크 클래스를 추가한다.
2. `IScheduledTask`를 구현하거나 `SchedulerTaskBase`를 상속한다.
3. `Name`, `Priority`, `Period`, `Mode`, `Status`, `ExecuteAsync`를 작성한다.
4. `Wpf.Lib.RTOS/Tasks/SchedulerMonitorDemo.cs`에서 등록 코드를 추가한다.

예시:

```csharp
public sealed class MyTask : SchedulerTaskBase
{
    private int _value;

    public MyTask()
        : base(
            "My Task",
            Enum_TaskPriority.Normal,
            TimeSpan.FromSeconds(1),
            Enum_TaskExecutionMode.Periodic,
            Enum_TaskOverrunPolicy.FixedRate)
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
