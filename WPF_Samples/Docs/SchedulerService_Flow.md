# SchedulerService 동작 흐름 설명

이 문서는 `SchedulerService`가 내부적으로 어떻게 동작하는지 설명합니다.

C# 초보자 기준으로 "메서드가 어떤 순서로 호출되는지"를 중심으로 설명합니다.

## 1. SchedulerService 생성

보통 ViewModel에서 다음처럼 생성합니다.

```csharp
private readonly SchedulerService _scheduler = new();
```

생성자의 기본값은 다음과 같습니다.

```text
tickInterval     10ms
snapshotInterval 100ms
```

`tickInterval`은 태스크 실행 여부를 확인하는 간격입니다.

`snapshotInterval`은 UI에 상태를 알려주는 간격입니다.

## 2. 태스크 등록

태스크는 `Register`로 등록합니다.

```csharp
_scheduler.Register(new CounterTask());
_scheduler.Register(new ClockTask());
_scheduler.Register(new UiRefreshTask());
```

등록된 태스크는 내부의 `_tasks` 목록에 들어갑니다.

동시에 `_runtimeInfos`에 실행 기록 저장 공간도 만들어집니다.

```text
_tasks
    실제 태스크 객체 목록

_runtimeInfos
    각 태스크의 실행 횟수, 마지막 실행 시간, 오류 정보
```

## 3. 스케줄러 시작

사용자가 Start 버튼을 누르면 ViewModel이 `_scheduler.Start()`를 호출합니다.

```csharp
private void StartScheduler()
{
    _scheduler.Start();
    RefreshCommandStates();
}
```

`Start` 내부에서는 다음 일이 일어납니다.

```text
1. 이미 실행 중인지 확인한다.
2. CancellationTokenSource를 만든다.
3. 상태를 Running으로 바꾼다.
4. Task.Run으로 RunAsync 루프를 백그라운드에서 시작한다.
5. 현재 상태 Snapshot을 한 번 발행한다.
```

`CancellationTokenSource`는 나중에 스케줄러를 멈출 때 사용합니다.

## 4. RunAsync 루프

`RunAsync`는 스케줄러의 반복 루프입니다.

간단히 말하면 아래 일을 계속 반복합니다.

```text
while 스케줄러가 취소되지 않았다면:
    현재 시각을 구한다.
    실행할 태스크를 고른다.
    고른 태스크를 실행한다.
    필요하면 화면 표시용 스냅샷을 만든다.
    tickInterval만큼 기다린다.
```

코드 흐름은 다음과 비슷합니다.

```csharp
while (!cancellationToken.IsCancellationRequested)
{
    var now = DateTimeOffset.Now;
    CopyRunnableTasks(now, _executionBuffer);

    foreach (var task in _executionBuffer)
    {
        await ExecuteTaskAsync(task, now, cancellationToken);
    }

    PublishSnapshotIfDue(now);
    await Task.Delay(_tickInterval, cancellationToken);
}
```

## 5. 실행 대상 태스크 고르기

`CopyRunnableTasks`는 현재 실행할 수 있는 태스크를 고릅니다.

조건은 다음과 같습니다.

```text
IsEnabled가 true
NextRunAt이 현재 시각보다 이전이거나 같음
```

즉 다음 실행 시간이 된 태스크만 실행 후보가 됩니다.

후보가 여러 개 있으면 다음 기준으로 정렬합니다.

```text
1. Priority가 높은 태스크 먼저
2. Priority가 같으면 NextRunAt이 빠른 태스크 먼저
```

## 6. 태스크 실행

태스크 실행은 `ExecuteTaskAsync`에서 처리합니다.

이 메서드는 실제 태스크의 `ExecuteAsync`를 호출하기 전후로 기록을 남깁니다.

```text
1. Stopwatch로 실행 시간 측정 시작
2. MarkTaskStarted 호출
3. task.ExecuteAsync 호출
4. 성공하면 MarkTaskCompleted 호출
5. 실패하면 MarkTaskFailed 호출
6. 다음 실행 시각 계산
```

태스크가 예외를 던져도 스케줄러 전체가 바로 죽지 않도록 예외를 잡습니다.

오류는 `SchedulerError` 이벤트로 외부에 알려줍니다.

## 7. 다음 실행 시각 계산

태스크 실행 후에는 `ScheduleNextRunIfRegistered`가 호출됩니다.

반복 태스크이면 다음 실행 시각은 다음처럼 계산됩니다.

```text
NextRunAt = 이번 기준 시각 + Period
```

예를 들어 현재 시각이 10:00:00이고 주기가 100ms이면 다음 실행 시각은 10:00:00.100입니다.

`OneShot` 태스크는 다시 실행되지 않도록 `DateTimeOffset.MaxValue`로 설정합니다.

## 8. 스냅샷 발행

`PublishSnapshotIfDue`는 UI에 상태를 알려줄 시간이 되었는지 확인합니다.

기본적으로 100ms마다 한 번씩 `SnapshotChanged` 이벤트를 발생시킵니다.

```text
SchedulerService
    SnapshotChanged 이벤트 발생
        |
        v
MainWindowViewModel
    Dispatcher로 UI 갱신 예약
```

스냅샷에는 다음 정보가 들어갑니다.

```text
스케줄러 실행 여부
태스크 이름
우선순위
주기
실행 횟수
최근 시작 시각
최근 완료 시각
실행 소요 시간
오류 메시지
상태 문자열
```

## 9. 스케줄러 정지

사용자가 Stop 버튼을 누르면 ViewModel이 `_scheduler.StopAsync()`를 호출합니다.

```csharp
private async Task StopSchedulerAsync()
{
    await _scheduler.StopAsync().ConfigureAwait(false);
    await _dispatcher.InvokeAsync(RefreshCommandStates);
}
```

`StopAsync`는 내부 `CancellationTokenSource`에 취소 요청을 보냅니다.

그러면 `RunAsync` 루프의 `Task.Delay`나 태스크 내부에서 cancellation token이 감지되어 루프가 종료됩니다.

정리 과정에서는 다음 일이 일어납니다.

```text
1. 상태를 Stopping으로 변경
2. CancellationTokenSource.Cancel 호출
3. RunAsync 종료 대기
4. CancellationTokenSource Dispose
5. 상태를 Stopped로 변경
6. 마지막 Snapshot 발행
```

## 10. 상태 전이

스케줄러 내부 상태는 다음 네 가지입니다.

```text
Stopped
Running
Stopping
Disposed
```

상태 전이는 보통 다음 순서로 일어납니다.

```text
Stopped -> Running -> Stopping -> Stopped
Stopped -> Running -> Disposed
Stopped -> Disposed
```

상태 변경은 `_stateSyncRoot` lock으로 보호됩니다.

이렇게 하면 Start와 Stop이 거의 동시에 호출되어도 상태가 꼬일 가능성을 줄일 수 있습니다.

## 11. 왜 lock을 사용하는가

스케줄러는 백그라운드 스레드에서 실행됩니다.

WPF ViewModel은 UI 스레드에서 스케줄러를 시작하거나 멈출 수 있습니다.

즉 여러 스레드가 같은 데이터를 만질 수 있습니다.

그래서 중요한 목록과 상태에는 lock을 사용합니다.

```text
_syncRoot
    태스크 목록과 실행 기록 보호

_stateSyncRoot
    스케줄러 실행 상태 보호
```

초보자에게는 lock을 "한 번에 한 사람만 들어갈 수 있는 문"으로 생각하면 쉽습니다.

## 12. Dispose와 DisposeAsync

`Dispose`는 스케줄러를 더 이상 사용하지 않을 때 정리하는 메서드입니다.

WPF 창이 닫힐 때 ViewModel에서 호출합니다.

```csharp
public void Dispose()
{
    _scheduler.SnapshotChanged -= OnSchedulerSnapshotChanged;
    _scheduler.Dispose();
}
```

`DisposeAsync`는 비동기로 정리할 수 있는 상황에서 사용합니다.

현재 샘플은 WPF 창 종료 시 동기 `Dispose`를 사용합니다.

## 핵심 요약

`SchedulerService`는 아래 세 가지를 계속 반복하는 관리자입니다.

```text
언제 실행할지 판단한다.
실행할 태스크를 실행한다.
상태를 Snapshot으로 알려준다.
```

---

## 2026-05-22 최신 동작 보강

### 1) StopAsync(TimeSpan timeout) 해석

현재 구현은 "종료 요청"과 "종료 대기"를 분리해 다룹니다.

1. Stop 요청: 내부 token cancel
2. 대기: timeout 안에 루프 종료를 기다림
3. timeout 초과 시: false 반환 가능

초보자 포인트:

- false는 "중지 요청 실패"가 아니라 "지정 시간 안에 완료 확인을 못함"에 가깝습니다.

### 2) 스케줄러 내부 자기중지 보호

스케줄러 실행 컨텍스트에서
무한 대기 StopAsync를 직접 호출하면 교착 위험이 있습니다.

현재 구현은 이 케이스를 보호하도록 되어 있습니다.

### 3) 협력형 선점 판단 위치

선점 판단은 두 단계로 이루어집니다.

1. `ShouldYieldToHigherPriorityTask`가 "더 높은 우선순위 + runnable"을 판단
2. 태스크 본문이 `context.ShouldYield()`를 호출해 실제 양보 여부를 결정

즉 스케줄러가 강제로 중단시키는 구조가 아니라,
태스크가 안전한 지점에서 스스로 양보하는 구조입니다.

### 4) 문서 읽기 보조 다이어그램

```text
Start()
    -> RunAsync loop
         -> CopyRunnableTasks
         -> ExecuteTaskAsync
                -> MarkStarted
                -> task.ExecuteAsync
                -> MarkCompleted or MarkFailed
                -> ScheduleNextRunIfRegistered
         -> PublishSnapshotIfDue
StopAsync(timeout)
    -> Cancel token
    -> Wait for loop termination (until timeout)
```

### 5) 초보자 디버깅 체크

1. Stop이 느리면: 태스크 내부 cancellationToken 확인 지점 점검
2. 실행 순서가 이상하면: Priority와 NextRunAt 값 점검
3. 선점이 약하면: LOW 태스크에서 `ShouldYield()` 호출 주기 점검
