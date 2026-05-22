# Wpf.Lib.RTOS 파일별 코드 설명

이 문서는 `Wpf.Lib.RTOS` 프로젝트의 파일을 하나씩 설명합니다.

## CommonRTOS.cs

이 파일에는 RTOS 라이브러리에서 사용하는 enum이 있습니다.

### Enum_TaskPriority

태스크 우선순위를 나타냅니다.

```csharp
public enum Enum_TaskPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}
```

같은 시점에 여러 태스크가 실행 대상이면 우선순위가 높은 태스크가 먼저 실행됩니다.

예를 들어 `High`는 `Normal`보다 먼저 실행됩니다.

### Enum_TaskExecutionMode

태스크 실행 방식을 나타냅니다.

```csharp
public enum Enum_TaskExecutionMode
{
    Periodic,
    OneShot
}
```

`Periodic`은 반복 실행 태스크입니다.

`OneShot`은 한 번만 실행되는 태스크입니다.

## IScheduledTask.cs

`IScheduledTask`는 태스크가 반드시 가져야 하는 기능을 정의한 인터페이스입니다.

인터페이스는 "이런 모양을 가진 클래스만 스케줄러에 등록할 수 있다"는 약속입니다.

```csharp
public interface IScheduledTask
{
    string Name { get; }
    Enum_TaskPriority Priority { get; }
    TimeSpan Period { get; }
    Enum_TaskExecutionMode Mode { get; }

    DateTimeOffset NextRunAt { get; set; }
    bool IsEnabled { get; }

    string Status { get; }

    Task ExecuteAsync(SchedulerContext context, CancellationToken cancellationToken);
}
```

각 속성의 의미는 다음과 같습니다.

| 이름 | 의미 |
| --- | --- |
| `Name` | 태스크 이름 |
| `Priority` | 태스크 우선순위 |
| `Period` | 반복 실행 주기 |
| `Mode` | 반복 실행인지, 한 번 실행인지 |
| `NextRunAt` | 다음 실행 예정 시각 |
| `IsEnabled` | 실행 가능한 상태인지 |
| `Status` | 화면에 표시할 현재 상태 문자열 |
| `ExecuteAsync` | 실제 태스크 작업을 수행하는 메서드 |

## SchedulerTaskBase.cs

`SchedulerTaskBase`는 태스크를 더 쉽게 만들기 위한 추상 기본 클래스입니다.

초보자 입장에서는 매번 `IScheduledTask`를 처음부터 직접 구현하기보다, 이 클래스를 상속해서 태스크를 만드는 것이 편합니다.

```csharp
public abstract class SchedulerTaskBase : IScheduledTask
```

이 클래스는 아래 기능을 미리 구현해 둡니다.

```text
Name
Priority
Period
Mode
NextRunAt
IsEnabled
Status
SetEnabled()
```

개발자는 실제 작업만 `ExecuteAsync`로 구현하면 됩니다.

```csharp
public abstract Task ExecuteAsync(
    SchedulerContext context,
    CancellationToken cancellationToken);
```

`NextRunAt`과 `IsEnabled`는 내부 lock으로 보호됩니다.

즉 여러 스레드에서 접근할 때 값이 중간에 꼬일 가능성을 줄입니다.

## ScheduledTask.cs

`ScheduledTask`는 간단한 태스크를 빠르게 만들기 위한 클래스입니다.

별도 클래스를 만들지 않고 delegate로 실행 코드를 넘길 수 있습니다.

```csharp
var task = new ScheduledTask(
    "My Task",
    Enum_TaskPriority.Normal,
    TimeSpan.FromSeconds(1),
    Enum_TaskExecutionMode.Periodic,
    async (context, token) =>
    {
        await DoSomethingAsync();
    });
```

현재 샘플에서는 직접 만든 `CounterTask`, `ClockTask`, `UiRefreshTask`를 사용하지만, 간단한 테스트 작업에는 `ScheduledTask`를 사용할 수 있습니다.

생성자에서는 다음 값을 검증합니다.

```text
name이 비어 있으면 예외
period가 0 이하이면 예외
executeAsync가 null이면 예외
```

## SchedulerContext.cs

`SchedulerContext`는 태스크가 실행될 때 스케줄러가 넘겨주는 정보입니다.

현재는 실행 기준 시각인 `Now`만 가지고 있습니다.

```csharp
public sealed class SchedulerContext
{
    public DateTimeOffset Now { get; }
}
```

태스크는 `DateTime.Now`를 직접 부르기보다 `context.Now`를 사용할 수 있습니다.

이렇게 하면 스케줄러가 판단한 실행 시각을 기준으로 작업할 수 있습니다.

## TaskRuntimeInfo.cs

`TaskRuntimeInfo`는 스케줄러 내부에서 태스크 실행 기록을 저장하는 클래스입니다.

외부에 공개되는 클래스가 아니라 `internal`입니다.

저장하는 정보는 다음과 같습니다.

```text
RunCount         실행 횟수
LastStartedAt    마지막 시작 시각
LastCompletedAt  마지막 완료 시각
LastDuration     마지막 실행 소요 시간
LastError        마지막 오류 메시지
```

태스크가 시작되면 `MarkStarted`가 호출됩니다.

태스크가 성공하면 `MarkCompleted`가 호출됩니다.

태스크가 실패하면 `MarkFailed`가 호출됩니다.

화면에 보여줄 때는 `ToSnapshot`으로 읽기용 복사본을 만듭니다.

## SchedulerSnapshot.cs

`SchedulerSnapshot`은 스케줄러 전체 상태를 담습니다.

```csharp
public sealed record SchedulerSnapshot(
    DateTimeOffset CreatedAt,
    bool IsRunning,
    IReadOnlyList<ScheduledTaskSnapshot> Tasks);
```

| 이름 | 의미 |
| --- | --- |
| `CreatedAt` | 스냅샷이 만들어진 시각 |
| `IsRunning` | 스케줄러 실행 여부 |
| `Tasks` | 태스크별 상태 목록 |

WPF 화면은 이 객체를 받아 DataGrid에 표시합니다.

## RtosSemaphore.cs

`RtosSemaphore`는 임베디드 RTOS의 semaphore 개념을 C#으로 흉내낸 클래스입니다.

세마포어는 동시에 사용할 수 있는 자원의 개수를 관리합니다.

예를 들어 자원이 1개뿐이면 첫 번째 태스크는 `WaitAsync`로 자원을 얻고, 두 번째 태스크는 자원이 반환될 때까지 기다립니다.

```csharp
using var semaphore = new RtosSemaphore(initialCount: 1, maxCount: 1);

var acquired = await semaphore.WaitAsync(TimeSpan.FromMilliseconds(100));
if (acquired)
{
    try
    {
        // 공유 자원 사용
    }
    finally
    {
        semaphore.Release();
    }
}
```

## RtosMessageQueue.cs

`RtosMessageQueue<T>`는 태스크 사이에 데이터를 주고받기 위한 메시지 큐입니다.

송신자는 `SendAsync`로 메시지를 넣고, 수신자는 `ReceiveAsync`로 메시지를 꺼냅니다.

큐가 가득 차면 송신자는 기다릴 수 있습니다.

큐가 비어 있으면 수신자는 기다릴 수 있습니다.

```csharp
using var queue = new RtosMessageQueue<int>(capacity: 4);

await queue.SendAsync(10, TimeSpan.FromMilliseconds(100));
var result = await queue.ReceiveAsync(TimeSpan.FromMilliseconds(100));
```

## RtosTickCounter.cs

`RtosTickCounter`는 RTOS의 tick 개념을 단순화한 클래스입니다.

tick은 RTOS가 시간을 세는 기본 단위입니다.

예를 들어 tick interval이 10ms이면 35ms가 지났을 때 현재 tick은 3입니다.

```csharp
var counter = new RtosTickCounter(TimeSpan.FromMilliseconds(10));
counter.Reset(DateTimeOffset.Now);

var tick = counter.AdvanceTo(DateTimeOffset.Now + TimeSpan.FromMilliseconds(35));
```

실제 임베디드 RTOS에서는 하드웨어 타이머 인터럽트가 tick을 증가시킵니다.

이 프로젝트에서는 학습용으로 시간과 tick의 관계를 계산하는 형태로 구현했습니다.

## ScheduledTaskSnapshot.cs

`ScheduledTaskSnapshot`은 태스크 하나의 상태를 담습니다.

```csharp
public sealed record ScheduledTaskSnapshot(
    string Name,
    Enum_TaskPriority Priority,
    TimeSpan Period,
    Enum_TaskExecutionMode Mode,
    DateTimeOffset NextRunAt,
    bool IsEnabled,
    string Status,
    long RunCount,
    DateTimeOffset? LastStartedAt,
    DateTimeOffset? LastCompletedAt,
    TimeSpan? LastDuration,
    string? LastError);
```

이 객체는 화면 표시용입니다.

스케줄러 내부의 실제 태스크 객체를 UI가 직접 만지지 않도록 중간 복사본 역할을 합니다.

## SchedulerService.cs

`SchedulerService`는 RTOS 라이브러리의 중심입니다.

담당하는 일은 다음과 같습니다.

```text
태스크 등록
태스크 제거
스케줄러 시작
스케줄러 정지
실행 대상 태스크 선택
태스크 실행
실행 기록 저장
스냅샷 생성
오류 이벤트 발생
```

주요 public 멤버는 다음과 같습니다.

| 멤버 | 설명 |
| --- | --- |
| `Register` | 태스크 등록 |
| `Unregister` | 태스크 제거 |
| `Clear` | 모든 태스크 제거 |
| `Start` | 스케줄러 시작 |
| `StopAsync` | 스케줄러 정지 |
| `SnapshotChanged` | 스냅샷 생성 이벤트 |
| `SchedulerError` | 오류 이벤트 |
| `IsRunning` | 실행 여부 |

스케줄러는 `Start`가 호출되면 백그라운드에서 `RunAsync` 루프를 실행합니다.

`RunAsync`는 계속 반복하면서 다음 일을 합니다.

```text
1. 현재 시각을 구한다.
2. 실행 시간이 된 태스크를 찾는다.
3. 우선순위와 다음 실행 시각 기준으로 정렬한다.
4. 태스크를 하나씩 실행한다.
5. 실행 기록을 저장한다.
6. 필요하면 SnapshotChanged 이벤트를 발생시킨다.
7. tickInterval만큼 기다린다.
```

기본 tick 간격은 10ms입니다.

기본 snapshot 간격은 100ms입니다.

즉 태스크 실행 여부는 10ms마다 확인하지만, UI 표시용 상태 이벤트는 100ms마다 발생합니다.

## 2026-05-22 최신 코드 기준 보완 포인트

아래 항목은 초보자가 문서를 읽을 때 헷갈리기 쉬운 부분이라 최신 코드 기준으로 정리한 내용입니다.

### 1) IScheduledTask에는 OverrunPolicy, SetEnabled가 포함됨

최신 `IScheduledTask`는 다음 멤버를 포함합니다.

- `Enum_TaskOverrunPolicy OverrunPolicy`
- `void SetEnabled(bool isEnabled)`

의미:

- OverrunPolicy: 주기를 넘겨 실행이 늦어진 경우 다음 실행 시각 계산 방식
- SetEnabled: 태스크 활성/비활성 토글

### 2) SchedulerContext에는 ShouldYield()가 있음

최신 `SchedulerContext`는 `Now`뿐 아니라 `ShouldYield()`를 제공합니다.

의미:

- 협력형 선점 모델에서,
  현재 태스크가 더 높은 우선순위 runnable 태스크에게 양보해야 하면 true를 반환

### 3) ScheduledTaskSnapshot 필드가 확장됨

현재 snapshot에는 단순 실행횟수 외에도 다음 통계가 포함됩니다.

- `State`
- `MinDuration`, `MaxDuration`, `AverageDuration`
- `LastStartDelay`, `MaxStartDelay`
- `DeadlineMissCount`

즉 화면에서 태스크의 "실행 횟수"뿐 아니라 "실행 품질"도 확인할 수 있습니다.

### 4) SchedulerService는 StopAsync(timeout) 경로를 지원

`StopAsync(TimeSpan timeout)`을 사용하면,

- 제한 시간 안에 종료되면 true
- 제한 시간을 넘기면 false

를 반환합니다.

초보자 팁:

- 태스크 코드에서 `cancellationToken`을 잘 관찰해야 Stop이 빨리 끝납니다.
## 2026-05-22 추가된 RTOS 시뮬레이션 파일

### RtosEventFlags.cs

RTOS event flag를 흉내내는 클래스입니다.

`Set`, `Clear`, `WaitAnyAsync`, `WaitAllAsync`를 제공합니다.

### RtosMutex.cs

RTOS mutex와 priority inheritance 개념을 흉내내는 클래스입니다.

현재 owner와 `EffectiveOwnerPriority`를 확인할 수 있습니다.

### RtosSoftwareTimer.cs

RTOS software timer를 흉내내는 클래스입니다.

one-shot timer와 periodic timer를 지원합니다.

### RtosTraceLog.cs

스케줄러와 태스크 실행 흔적을 보관하는 ring buffer 형태의 trace log입니다.

`SchedulerService.TraceLog`에서 snapshot으로 확인할 수 있습니다.
