# RTOS 테스트 실행 가이드

이 문서는 `WpfSamples/Tests` 폴더의 테스트 코드와 WPF UI 테스트 실행 방법을 설명합니다.

## 테스트 코드 위치

테스트 코드는 아래 폴더에 있습니다.

```text
WpfSamples
 └─ Tests
    ├─ RtosAssert.cs
    ├─ RtosTestResult.cs
    └─ RtosTestRunner.cs
```

별도의 xUnit, NUnit 같은 외부 테스트 프레임워크를 사용하지 않았습니다.

WPF 샘플 프로그램 안에서 바로 실행할 수 있도록 간단한 테스트 러너를 직접 만들었습니다.

## 테스트 실행 방법

1. `WpfSamples` 프로그램을 실행합니다.
2. 상단 탭에서 `RTOS Tests`를 선택합니다.
3. `Run Tests` 버튼을 누릅니다.
4. 아래 DataGrid에서 테스트 결과를 확인합니다.

결과는 다음 컬럼으로 표시됩니다.

| 컬럼 | 설명 |
| --- | --- |
| `Test` | 테스트 이름 |
| `Result` | PASS 또는 FAIL |
| `Duration` | 테스트 실행 시간 |
| `Message` | 성공 메시지 또는 실패 이유 |

## 현재 포함된 테스트

### Semaphore wait/release

`RtosSemaphore`가 정상적으로 자원을 획득하고 반환하는지 확인합니다.

검증 내용:

```text
초기 count가 1이면 첫 WaitAsync는 성공해야 한다.
count가 0이면 다음 WaitAsync는 timeout되어야 한다.
Release 후에는 다시 WaitAsync가 성공해야 한다.
```

### MessageQueue send/receive

`RtosMessageQueue<T>`가 메시지를 넣고 꺼낼 수 있는지 확인합니다.

검증 내용:

```text
빈 큐에 SendAsync는 성공해야 한다.
가득 찬 큐에 SendAsync는 timeout되어야 한다.
ReceiveAsync로 송신한 값을 다시 받을 수 있어야 한다.
```

### TickCounter converts time to ticks

`RtosTickCounter`가 시간 차이를 tick 값으로 바르게 변환하는지 확인합니다.

검증 내용:

```text
tick interval이 10ms일 때 35ms는 3 tick이어야 한다.
3 tick의 시간은 시작 시각 + 30ms이어야 한다.
```

### Scheduler executes periodic task

`SchedulerService`가 등록된 주기 태스크를 실제로 반복 실행하는지 확인합니다.

검증 내용:

```text
10ms 주기 태스크를 등록한다.
스케줄러를 약 60ms 실행한다.
태스크가 2회 이상 실행되었는지 확인한다.
```

### Scheduler snapshot contains task state

스케줄러 snapshot에 태스크 상태가 들어오는지 확인합니다.

검증 내용:

```text
태스크를 등록하고 스케줄러를 실행한다.
SnapshotChanged 이벤트를 통해 snapshot을 받는다.
snapshot 안에 등록한 태스크가 있는지 확인한다.
태스크 상태가 Ready 또는 Blocked인지 확인한다.
```

## 새 테스트 추가 방법

새 테스트는 `RtosTestRunner.cs`에 추가합니다.

1. 테스트 메서드를 만든다.
2. `RunAllAsync`의 tests 배열에 추가한다.

예시:

```csharp
private static Task MyNewTestAsync()
{
    RtosAssert.True(1 + 1 == 2, "기본 산술 테스트");
    return Task.CompletedTask;
}
```

등록:

```csharp
var tests = new (string Name, Func<Task> Execute)[]
{
    ("My new test", MyNewTestAsync),
};
```

테스트가 예외 없이 끝나면 PASS입니다.

테스트 중 예외가 발생하면 FAIL로 표시되고, 예외 메시지가 UI에 표시됩니다.
## 2026-05-22 추가 테스트

RTOS 동작 검증을 위해 다음 테스트가 추가되었습니다.

```text
Semaphore wait observes cancellation
MessageQueue preserves FIFO order
Scheduler stop honors cancellation
Scheduler unregister prevents future execution
Scheduler clear removes all tasks
Scheduler reports task errors
Scheduler survives snapshot handler errors
Scheduler fixed-delay overrun waits after completion
Scheduler skip-missed overrun advances schedule
```

특히 overrun 테스트는 태스크 실행 시간이 주기보다 길어졌을 때 다음 실행 시각을 어떻게 계산할지 확인합니다.

`FixedDelay`는 태스크 완료 시각 뒤에 period를 더합니다.

`SkipMissedTicks`는 놓친 주기를 건너뛰고 완료 시각 이후의 다음 주기로 이동합니다.
## 2026-05-22 RTOS primitive 추가 테스트

다음 RTOS 시뮬레이션 기능과 테스트가 추가되었습니다.

```text
EventFlags wait-any completes when flag is set
EventFlags wait-all waits for all flags
Mutex tracks owner and priority inheritance
SoftwareTimer one-shot fires once
SoftwareTimer periodic fires repeatedly
Scheduler records trace entries
Scheduler snapshot contains runtime statistics
```

`RtosEventFlags`는 여러 bit 이벤트를 기다리는 기능입니다.

`RtosMutex`는 owner 추적과 priority inheritance 개념을 흉내냅니다. 실제 Windows 스케줄러의 우선순위를 바꾸는 것은 아니고, RTOS에서 발생하는 priority inversion 상황을 학습하기 위한 상태 모델입니다.

`RtosSoftwareTimer`는 `Task.Delay` 기반 one-shot/periodic timer입니다. 실제 RTOS timer interrupt처럼 정확한 실시간 보장은 하지 않습니다.

`SchedulerService.TraceLog`는 태스크 등록, 시작, 완료, 실패 같은 실행 흔적을 보관합니다.

`ScheduledTaskSnapshot`에는 실행시간 통계와 deadline miss count가 추가되었습니다.
