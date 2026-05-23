# C# 주니어용 RTOS 스케줄러 구현 매뉴얼

이 문서는 "현재 Wpf.Lib.RTOS를 참고해 다른 프로젝트에서 별도 스케줄러를 직접 구현"할 수 있도록 만든 단계별 매뉴얼입니다.

핵심 목표는 두 가지입니다.

1. 선점/스케줄링 코드가 C#에서 어떻게 동작하는지 이해한다.
2. 초기 구현부터 테스트 고도화까지 실제 구현 순서를 따라 재현한다.

이 문서는 설명 중심이 아니라 "구현 순서 + 왜 이렇게 설계했는가 + 검증 방법" 중심으로 작성했습니다.

---

## 1. 먼저 전제: C#에서 RTOS를 어떻게 해석해야 하는가

일반 PC/.NET 환경에서 임베디드 RTOS의 하드 실시간, 인터럽트 기반 강제 선점을 그대로 재현할 수는 없습니다.

따라서 이 프로젝트의 RTOS 모델은 아래처럼 정의합니다.

1. 실행 주기와 우선순위를 가진 태스크를 통합 관리한다.
2. 스케줄러가 runnable 후보를 고르고 순서를 정한다.
3. 실행 중 태스크는 SchedulerContext.ShouldYield()로 협력형 양보를 수행한다.
4. timeQuantum으로 "강제 선점에 가까운" 취소 경계를 제공한다.

왜 이 구조인가:

1. .NET Task 기반과 자연스럽게 결합 가능
2. WPF/서버/백그라운드 앱에서 동일한 패턴으로 사용 가능
3. 테스트로 동작을 안정적으로 고정할 수 있음

---

## 2. 전체 아키텍처 (다른 프로젝트로 이식할 때의 최소 세트)

필수 구성요소:

1. IScheduledTask
2. SchedulerTaskBase
3. SchedulerContext
4. SchedulerService
5. TaskRuntimeInfo + Snapshot 모델
6. 테스트 러너(최소 smoke + 시나리오 회귀)

권장 디렉터리 구조:

```text
MyCompany.Rtos
  IScheduledTask.cs
  SchedulerTaskBase.cs
  SchedulerContext.cs
  SchedulerService.cs
  TaskRuntimeInfo.cs
  SchedulerSnapshot.cs
  ScheduledTaskSnapshot.cs
  Primitives/
    RtosSemaphore.cs
    RtosMutex.cs
    RtosEventFlags.cs
    RtosMessageQueue.cs
    RtosSoftwareTimer.cs
```

현재 프로젝트 대응 파일:

1. Wpf.Lib.RTOS/SchedulerService.cs
2. Wpf.Lib.RTOS/SchedulerContext.cs
3. Wpf.Lib.RTOS/IScheduledTask.cs
4. Wpf.Lib.RTOS/SchedulerTaskBase.cs
5. WpfSamples/Tests/RtosTestRunner.cs

---

## 3. 0단계: 요구사항부터 고정하기

코드보다 먼저 아래 요구사항을 명확히 문서화합니다.

1. 태스크는 Periodic/OneShot을 지원해야 한다.
2. 우선순위가 높을수록 먼저 실행되어야 한다.
3. 태스크 실행 시간 오버런 시 정책(FixedRate/FixedDelay/SkipMissedTicks)을 지원해야 한다.
4. stop/dispose 시 hang 없이 종료되어야 한다.
5. 운영 관찰용 스냅샷과 오류 이벤트가 있어야 한다.
6. 선점은 협력형 + timeQuantum 근사 모델로 정의한다.

왜 먼저 요구사항을 고정하는가:

1. 나중에 정렬/다음 실행 시각 계산이 바뀌어도 테스트 기준이 흔들리지 않음
2. "버그인지 정책인지"를 구분하기 쉬움

---

## 4. 1단계: 태스크 계약과 기본 타입 만들기

먼저 `IScheduledTask`를 확정합니다.

필수 속성:

1. Name
2. Priority
3. Period
4. Mode
5. OverrunPolicy
6. NextRunAt
7. IsEnabled

필수 메서드:

1. ExecuteAsync(SchedulerContext, CancellationToken)
2. SetEnabled(bool)

다음으로 `SchedulerTaskBase`를 구현해 공통 상태(`NextRunAt`, `IsEnabled`)를 lock으로 보호합니다.

왜 Base 클래스가 필요한가:

1. 태스크 구현자가 동시성 실수를 덜 하게 됨
2. 태스크 코드가 업무 로직에만 집중 가능

---

## 5. 2단계: SchedulerContext로 선점 인터페이스 열기

`SchedulerContext`는 태스크에 전달되는 실행 컨텍스트입니다.

핵심 API:

1. Now
2. ShouldYield()
3. MarkPreempted()

태스크 작성 규칙:

1. 긴 루프 내부에서 주기적으로 ShouldYield() 확인
2. true면 현재 작업 단위를 저장/정리 후 빠르게 반환
3. cancellationToken도 함께 확인

왜 context 객체를 따로 두는가:

1. 스케줄러 내부 로직과 태스크 로직을 느슨하게 분리
2. 나중에 실행 메타데이터를 추가해도 API 확장이 쉬움

---

## 6. 3단계: SchedulerService 최소 루프 구현

최초 버전은 아래 루프만 구현합니다.

1. 현재 시각 획득
2. runnable 후보 선별
3. 후보 실행
4. 다음 실행 시각 계산
5. tick 지연

최초 pseudocode:

```csharp
while (!token.IsCancellationRequested)
{
    var now = DateTimeOffset.Now;
    var runnable = tasks.Where(t => t.IsEnabled && t.NextRunAt <= now)
                        .OrderByDescending(t => t.Priority)
                        .ThenBy(t => t.NextRunAt)
                        .ToList();

    foreach (var task in runnable)
    {
        await ExecuteTaskAsync(task, now, token);
    }

    await Task.Delay(tickInterval, token);
}
```

이 시점에서 먼저 확인할 것:

1. 주기 실행이 되는가
2. 우선순위 역전 없이 높은 우선순위가 먼저 실행되는가
3. stop이 정상 종료되는가

---

## 7. 4단계: 현재 프로젝트 수준으로 고도화하기

현재 `SchedulerService`는 단순 리스트 스캔보다 고도화된 구조를 사용합니다.

핵심 고도화 4가지:

1. due-task PriorityQueue 도입
2. stale priority 정합성 재검증
3. disabled revisit 내부 관리
4. snapshot 주기 발행

### 7-1. due queue를 쓰는 이유

문제:

1. 매 loop마다 전체 task scan/sort는 비효율적

해결:

1. `PriorityQueue<IScheduledTask, DueTaskPriority>`로 "가장 빨리 실행할 태스크"를 빠르게 조회

주의:

1. task의 Priority/NextRunAt이 바뀌면 queue 내부 우선순위는 stale될 수 있음
2. 따라서 peek 시점에 현재 값과 queued priority를 비교해 불일치면 재큐잉

### 7-2. runnable 정렬 정책(현재 코드)

`CompareRunnableTasks`는 단순 priority-first가 아닙니다.

1. due-time 차이가 tick보다 충분히 크면 due-time 우선
2. 차이가 작으면 priority 우선

왜 이렇게 했는가:

1. 선점 응답성과 시간 정합성의 균형
2. 근접 due 태스크에서 우선순위 의미를 살리면서, 크게 늦은 태스크 starvation 완화

### 7-3. ExecuteTaskAsync의 상태 기계

실행 결과를 3경로로 분기합니다.

1. 정상 완료
2. quantum 선점 취소(OperationCanceledException with quantum token)
3. 실패 예외

각 경로에서 공통으로 하는 작업:

1. 실행 시간 측정
2. runtimeInfo 업데이트
3. trace 기록
4. 다음 실행 시각 스케줄링

### 7-4. NextRun 계산 정책

`ScheduleNextRunIfRegistered`는 정책의 중심입니다.

1. OneShot: 비활성화 + MaxValue
2. Periodic + preempted: completedAt 즉시 재예약
3. Periodic + normal: OverrunPolicy 적용

---

## 8. 5단계: 선점(Preemption) 구현 디테일

### 8-1. 협력형 선점

핵심 메서드:

1. `ShouldYieldToHigherPriorityTask(runningTask)`

판단 조건:

1. runningTask보다 우선순위가 높은 태스크가 존재
2. 그 태스크가 `NextRunAt <= now`로 runnable 상태

태스크 코드에서의 사용 패턴 예시:

```csharp
while (workRemaining)
{
    cancellationToken.ThrowIfCancellationRequested();

    DoSmallWorkUnit();

    if (context.ShouldYield())
    {
        return;
    }
}
```

### 8-2. timeQuantum 근사 선점

`SchedulerService(timeQuantum: ...)`가 설정되면,
`ExecuteTaskAsync`에서 linked CTS를 만들고 `CancelAfter(timeQuantum)`를 적용합니다.

의미:

1. 진짜 인터럽트 선점은 아니지만, 긴 실행을 취소 경계로 잘라 선점 유사 효과를 냄
2. 태스크는 cancellation-friendly하게 작성되어야 효과가 있음

---

## 9. 6단계: 동기화 Primitive를 붙일 때의 핵심 규칙

이 단계에서 많은 버그가 납니다. 반드시 아래를 지킵니다.

1. timeout / cancellation / dispose 의미를 분리
2. dispose 시 대기중 WaitAsync 경합을 고려
3. lock 안에서 await 금지

현재 코드에서 중요한 사례:

1. MessageQueue dispose 경로
- shutdown 취소 신호로 pending wait를 깨움
- dispose 경합으로 테스트 timeout이 나지 않도록 경로 정리

2. Mutex 정책
- same-owner 재진입 금지로 데드락/정책 혼선 방지

3. SoftwareTimer 수명주기
- dispose 후 Start/Stop 호출을 명시적으로 차단

---

## 10. 7단계: 관찰성과 운영성 추가

상용 앱에서 실제로 중요한 부분입니다.

1. SnapshotChanged 이벤트
2. SchedulerError 이벤트
3. TraceLog
4. TaskRuntimeInfo 통계

왜 필요한가:

1. "실행은 되는데 왜 느린지"를 수치로 파악 가능
2. 장애 시 태스크 단위로 원인 추적 가능

현재 스냅샷에 포함되는 대표 값:

1. run count
2. last duration / min / max / average
3. deadline miss count
4. last error
5. start/done thread id

### 10-0. 상태(State) 정의를 코드로 고정하기

재구현 시 가장 먼저 맞춰야 하는 부분 중 하나가 상태 의미입니다.

권장 정의(현재 프로젝트와 동일):

1. `Running`
- 현재 실행 중인 태스크

2. `Ready`
- 실행 가능 상태
- `IsEnabled == true` && `NextRunAt <= now`

3. `Blocked`
- 실행 대기 상태(주기 도래 전)
- `IsEnabled == true` && `NextRunAt > now`

4. `Suspended`
- 실행 비활성 상태
- `IsEnabled == false` 또는 OneShot 완료

중요:

1. 이 상태는 OS 스레드 상태와 다릅니다.
2. 스케줄러 정책을 설명하기 위한 도메인 상태입니다.

### 10-0-1. 상태 전이 표

| 현재 | 조건 | 다음 |
| --- | --- | --- |
| Ready | 스케줄러가 선택해 실행 시작 | Running |
| Running | Periodic 완료 + nextRunAt > now | Blocked |
| Running | Periodic 완료 + nextRunAt <= now | Ready |
| Running | OneShot 완료 | Suspended |
| Blocked | 시간이 흘러 nextRunAt <= now | Ready |
| Ready/Blocked | SetEnabled(false) | Suspended |
| Suspended | SetEnabled(true) + nextRunAt > now | Blocked |
| Suspended | SetEnabled(true) + nextRunAt <= now | Ready |

실무 체크 포인트:

1. 상태 계산 로직은 스냅샷 생성 시점에 단일 함수로 모아두세요.
2. UI에서 상태를 보고 디버깅할 때는 RunCount/LastStartedAt과 함께 해석하세요.
3. 선점 데모처럼 주기가 짧은 구성에서는 Ready/Blocked 전환이 빠르게 반복되어 정상입니다.

### 10-1. 기아(starvation) 경고를 운영 신호로 추가하기

선점 데모/운영 모니터에서 아래 조합을 함께 보면 기아 탐지가 실용적입니다.

1. LOW RunCount 증가 여부
2. LOW LastStartedAt 최신성
3. HIGH/NORMAL RunCount 증가 여부
4. 상위 우선순위 task의 Ready/Running 지속 여부

판정 예시:

1. LOW RunCount가 일정 시간 증가하지 않음
2. LOW LastStartedAt도 같은 시간 동안 갱신되지 않음
3. 같은 기간 HIGH/NORMAL은 계속 진행됨

위 조건이 동시에 성립하면 "LOW가 준비되더라도 실행 기회를 얻지 못하는 구간"으로 볼 수 있습니다.

실무 팁:

1. 경고 임계값은 고정하지 말고 조절 가능하게 두세요.
2. 이 프로젝트는 선점 데모 UI에 "기아 감지 민감도" 슬라이더를 추가해 판정 지연(초)을 런타임에 조정할 수 있게 했습니다.
3. 기본 3초를 시작점으로, 환경에 따라 1~8초 범위에서 튜닝합니다.

---

## 10-2. UI 끊김 방지 원칙 (데모/운영 공통)

선점/모니터링 화면이 무거워지지 않게 하려면 아래를 지키는 것이 중요합니다.

1. 로그는 건별 출력 대신 누적 요약 출력
2. 컬렉션은 값이 바뀐 경우에만 재구성
3. 타이머 tick에서 처리할 최대 작업량을 제한

현재 프로젝트의 적용 예:

1. QueuePreemptionLogEvery로 이벤트를 N회 누적 후 1회 출력
2. ActiveTasks 집합이 동일하면 RebuildActiveTasks 생략
3. pending 로그 큐 상한/틱당 배출 제한으로 UI 프레임 드랍 완화
4. 선점 데모 하단 로그는 1초 간격으로 누적 요약 1건만 ListBox에 반영

추가 설명:

1. UI 끊김이 남으면 먼저 로그 append 빈도를 확인하세요.
2. ListBox/ObservableCollection에 대한 고빈도 Insert는 체감 끊김으로 바로 나타납니다.
3. 따라서 "생성 빈도 제한 + 1초 배치 요약" 조합이 가장 비용 대비 효과가 큽니다.

---

## 11. 8단계: 테스트를 구현 단계와 같이 성장시키기

아래 순서로 테스트를 추가하면 구현 안정성이 급격히 올라갑니다.

### 단계 A: 기본 동작

1. periodic 실행
2. priority 실행 순서
3. one-shot 1회 실행

### 단계 B: 수명주기/예외

1. stop timeout
2. stop cancellation
3. unregister/clear
4. scheduler error 보고

### 단계 C: 선점/오버런

1. cooperative preemption yield
2. preempted task immediate resume
3. time quantum preemption
4. fixed-delay overrun
5. skip-missed overrun

### 단계 D: primitive 안전성

1. semaphore over-release
2. message queue FIFO + timeout + dispose pending wait
3. event flags wait-any/wait-all + dispose cancel
4. mutex owner/priority inheritance + reentrancy policy
5. software timer periodic/one-shot/disposing race

현재 프로젝트 기준 전체 회귀는 `WpfSamples/Tests/RtosTestRunner.cs`에 통합돼 있으며,
CLI에서는 아래로 검증합니다.

```bash
dotnet run --project WpfSamples.Tests.Cli/WpfSamples.Tests.Cli.csproj -- --all
```

특정 회귀만 빠르게 검증:

```bash
dotnet run --project WpfSamples.Tests.Cli/WpfSamples.Tests.Cli.csproj -- --tests MessageQueuePendingSendCanceledByDisposeAsync,MessageQueuePendingReceiveCanceledByDisposeAsync
```

---

## 12. 처음부터 끝까지 구현 로드맵 (실행 체크리스트)

아래 순서를 그대로 따라가면 "재구현 + 검증"이 가능합니다.

1. Enum/IScheduledTask/SchedulerTaskBase 정의
2. SchedulerContext 정의(ShouldYield/MarkPreempted 포함)
3. SchedulerService 최소 루프 구현(Start/Stop/Register/Run)
4. ExecuteTaskAsync + runtimeInfo + next-run 계산 추가
5. Snapshot/Trace/Error 이벤트 추가
6. due queue 최적화 및 stale 재검증 로직 추가
7. cooperative preemption + time quantum 도입
8. primitive 연결(semaphore/mutex/eventflags/msgqueue/timer)
9. 테스트 러너 구성(기본 -> 회귀 -> 경합 시나리오)
10. CLI 반복 실행/비교 모드 추가

각 단계 완료 정의(Definition of Done):

1. 기능 동작
2. 실패 시 오류가 의미 있게 보고됨
3. 최소 1개 이상 회귀 테스트로 동작이 고정됨

---

## 13. 왜 현재 구조가 유지보수에 유리한가

1. 계약(IScheduledTask)과 엔진(SchedulerService) 분리
2. 정책(Overrun/Preemption)이 메서드 단위로 명확히 분리
3. 관찰성(Snapshot/Trace/Error)이 내장되어 장애 분석이 빠름
4. 테스트가 기능 단위로 촘촘해 변경 시 회귀를 빨리 감지

즉, 주니어가 수정할 때도 "한 곳을 바꾸고 어떤 테스트가 깨지는지"가 명확해집니다.

---

## 14. 주니어가 반드시 피해야 할 안티패턴

1. lock 내부에서 await
2. dispose 시 동시 대기 작업을 고려하지 않음
3. timeout/cancel/dispose 예외 의미를 섞음
4. 우선순위 정책을 바꾸면서 queue stale 판정 로직은 안 바꿈
5. 테스트 없이 정책 변경

---

## 15. 마지막 요약

이 매뉴얼의 핵심은 "코드 읽기"가 아니라 "재구현 가능한 설계 습관"입니다.

1. 정책을 먼저 정의하고
2. 작은 루프부터 구현한 뒤
3. 선점/오버런/수명주기 경합을 단계적으로 추가하고
4. 테스트로 동작을 고정하세요.

이 순서를 지키면, 현재 코드베이스를 기반으로 다른 프로젝트에서도 충분히 별도 RTOS 스케줄러를 구현할 수 있습니다.
