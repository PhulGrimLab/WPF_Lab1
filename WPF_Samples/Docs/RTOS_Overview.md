# Wpf.Lib.RTOS 개요

## 안내

더 자세한 초보자용 통합 설명서는 다음 문서를 참고하세요.

- Docs/RTOS_Beginner_Complete_Guide.md
- Docs/RTOS_Primitives_Deep_Dive.md
- Docs/RTOS_Simulator_Maturity_Checklist.md

이 문서는 `Wpf.Lib.RTOS` 라이브러리를 C# 초보자도 이해할 수 있도록 설명하기 위한 문서입니다.

이 라이브러리는 실제 RTOS를 그대로 구현한 것은 아니고, WPF 샘플 프로그램에서 여러 작업을 정해진 주기와 우선순위에 따라 실행하기 위한 "RTOS 스타일 스케줄러"입니다.

## RTOS 스타일이라는 뜻

RTOS는 Real-Time Operating System의 줄임말입니다.

일반적으로 RTOS에서는 여러 작업을 "태스크"라고 부르고, 각 태스크는 정해진 주기나 우선순위에 따라 실행됩니다.

이 프로젝트의 `Wpf.Lib.RTOS`도 비슷한 생각을 사용합니다.

```text
SchedulerService
    |
    +-- CounterTask
    +-- ClockTask
    +-- UiRefreshTask
```

`SchedulerService`는 등록된 태스크들을 계속 확인하다가 실행할 시간이 된 태스크를 실행합니다.

## 핵심 용어

### Scheduler

스케줄러는 "어떤 태스크를 언제 실행할지 결정하는 관리자"입니다.

이 프로젝트에서는 `SchedulerService` 클래스가 스케줄러 역할을 합니다.

### Task

태스크는 스케줄러가 실행하는 하나의 작업 단위입니다.

예를 들면 다음과 같습니다.

```text
CounterTask    100ms마다 카운터 증가
ClockTask      1초마다 현재 시간 갱신
UiRefreshTask  250ms마다 갱신성 작업 실행
```

### Period

`Period`는 태스크가 반복 실행되는 주기입니다.

예를 들어 `TimeSpan.FromMilliseconds(100)`이면 100ms마다 실행됩니다.

### Priority

`Priority`는 같은 시점에 여러 태스크가 실행 대상이 되었을 때 먼저 실행할 순서를 정합니다.

우선순위는 다음 enum으로 표현합니다.

```csharp
public enum Enum_TaskPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}
```

값이 클수록 더 높은 우선순위입니다.

### Snapshot

스냅샷은 현재 스케줄러와 태스크들의 상태를 한 번에 담은 읽기 전용 정보입니다.

WPF 화면은 스케줄러 내부 객체를 직접 만지지 않고, `SchedulerSnapshot`을 받아 화면에 표시합니다.

## 전체 실행 흐름

아래 흐름으로 동작합니다.

```text
1. WPF ViewModel이 SchedulerService를 생성한다.
2. CounterTask, ClockTask, UiRefreshTask를 등록한다.
3. 사용자가 Start 버튼을 누른다.
4. SchedulerService가 백그라운드 루프를 시작한다.
5. 스케줄러는 실행 시간이 된 태스크를 찾는다.
6. 우선순위가 높은 태스크부터 실행한다.
7. 실행 결과와 상태를 Snapshot으로 만든다.
8. WPF ViewModel이 Snapshot을 받아 DataGrid를 갱신한다.
9. 사용자가 Stop 버튼을 누르면 스케줄러가 취소된다.
```

## WPF에서 중요한 점

WPF 화면은 UI 스레드에서만 안전하게 갱신할 수 있습니다.

하지만 `SchedulerService`는 백그라운드에서 실행됩니다.

그래서 샘플 프로그램에서는 다음처럼 역할을 나눕니다.

```text
SchedulerService
    백그라운드에서 태스크 실행

MainWindowViewModel
    SnapshotChanged 이벤트를 받고 Dispatcher를 통해 UI 갱신

MainWindow.xaml
    DataGrid로 태스크 상태 표시
```

## 초보자가 먼저 봐야 할 파일

처음 볼 때는 아래 순서로 보는 것이 좋습니다.

1. `IScheduledTask.cs`
2. `SchedulerTaskBase.cs`
3. `SchedulerService.cs`
4. `ScheduledTaskSnapshot.cs`
5. `WpfSamples/Samples_RTOS/CounterTask.cs`
6. `WpfSamples/MainWindowViewModel.cs`

`IScheduledTask`는 "태스크가 갖춰야 할 약속"이고, `SchedulerService`는 그 약속을 가진 태스크들을 실제로 실행하는 관리자입니다.
