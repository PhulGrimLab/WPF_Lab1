# C# 주니어용 RTOS 스케줄러 Hands-on Labs (3단계)

이 문서는 "다른 프로젝트에서 RTOS 스타일 스케줄러를 직접 구현"하기 위한 실습 패키지입니다.

구성은 3단계입니다.

1. Lab 1: 최소 스케줄러 구현
2. Lab 2: 선점(협력형 + time quantum 근사) 구현
3. Lab 3: 현재 프로젝트 수준 테스트 케이스 적용

완료 기준은 간단합니다.

1. 각 단계의 체크리스트를 통과한다.
2. 마지막에 전체 회귀 테스트를 통과한다.

---

## 0. 실습 준비

필수 선행 문서:

1. Docs/RTOS_Scheduler_Implementation_Manual_For_Juniors.md
2. Docs/RTOS_Junior_Code_Maintenance_Guide.md

권장 기준 프로젝트 구조:

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
```

---

## Lab 1. 최소 스케줄러 구현

목표:

1. Periodic/OneShot 실행
2. 우선순위 정렬
3. Start/Stop 동작
4. 기본 스냅샷 발행

### 1-1. 구현 순서

1. `IScheduledTask` 구현
2. `SchedulerTaskBase` 구현
3. `SchedulerContext` 구현(`Now`만 있어도 시작 가능)
4. `SchedulerService` 최소 루프 구현

최소 루프 예시:

```csharp
while (!token.IsCancellationRequested)
{
    var now = DateTimeOffset.Now;
    var runnable = tasks
        .Where(t => t.IsEnabled && t.NextRunAt <= now)
        .OrderByDescending(t => t.Priority)
        .ThenBy(t => t.NextRunAt)
        .ToList();

    foreach (var task in runnable)
    {
        await task.ExecuteAsync(new SchedulerContext(now), token).ConfigureAwait(false);
        ScheduleNextRun(task, now);
    }

    await Task.Delay(tickInterval, token).ConfigureAwait(false);
}
```

### 1-2. 왜 이 구조인가

1. 구현 난이도가 낮아 초기에 정책 검증이 빠름
2. 태스크 계약과 실행 엔진이 분리되어 확장이 쉬움
3. 테스트로 동작 고정하기 좋음

### 1-3. Lab 1 완료 체크리스트

1. 동일 주기 태스크 2개 등록 시 높은 Priority가 먼저 실행됨
2. OneShot 태스크가 정확히 1회만 실행됨
3. Stop 시 루프가 종료되고 다시 Start 가능함
4. SnapshotChanged 이벤트로 최소 상태가 전달됨

---

## Lab 2. 선점/스케줄링 고도화

목표:

1. 협력형 선점(ShouldYield)
2. timeQuantum 기반 근사 선점
3. 오버런 정책(FixedRate/FixedDelay/SkipMissedTicks)
4. 실행 통계 수집(TaskRuntimeInfo)

### 2-1. 구현 순서

1. `SchedulerContext`에 `ShouldYield()`/`MarkPreempted()` 추가
2. `SchedulerService.ShouldYieldToHigherPriorityTask()` 구현
3. `ExecuteTaskAsync()`에 timeQuantum linked CTS 추가
4. 선점/실패/완료 3경로 통계 업데이트
5. `ScheduleNextRunIfRegistered()`에 오버런 정책 반영

선점 체크 패턴(태스크 코드):

```csharp
while (hasWork)
{
    cancellationToken.ThrowIfCancellationRequested();
    ProcessSmallChunk();

    if (context.ShouldYield())
    {
        return;
    }
}
```

### 2-2. 왜 이 구조인가

1. C#에서 강제 선점 대신 안전한 협력형 모델을 구현 가능
2. quantum으로 긴 작업을 강제로 끊는 효과를 근사할 수 있음
3. 오버런 정책 분리로 운영 요구사항에 맞는 조정이 가능

### 2-3. Lab 2 완료 체크리스트

1. LOW 실행 중 HIGH runnable이 되면 LOW가 양보함
2. 선점된 LOW는 즉시 재스케줄되어 이어서 실행됨
3. timeQuantum 설정 시 긴 태스크가 취소 경계에서 끊김
4. FixedRate/FixedDelay/SkipMissedTicks 차이가 로그/스냅샷에서 확인됨

---

## Lab 3. 현재 프로젝트 수준 테스트 케이스 적용

목표:

1. primitive 안정성(timeout/cancel/dispose 분리)
2. 스케줄러 회귀 테스트 고정
3. 전체 테스트 자동 검증 루틴 확보

### 3-1. 테스트 묶음 적용 순서

Phase A (기본):

1. Scheduler executes periodic task
2. Scheduler runs higher priority task first
3. Scheduler executes one-shot task once

Phase B (선점/오버런):

1. Scheduler cooperative preemption yields to higher priority
2. Scheduler preempted task resumes immediately
3. Scheduler time quantum preempts long task
4. Scheduler fixed-delay overrun waits after completion
5. Scheduler skip-missed overrun advances schedule

Phase C (수명주기/안정성):

1. Scheduler stop timeout returns false
2. Scheduler stop honors cancellation
3. Scheduler unregister prevents future execution
4. Scheduler clear removes all tasks
5. Scheduler reports task errors

Phase D (Primitive):

1. Semaphore wait/release + over-release + dispose
2. MessageQueue FIFO + timeout + pending wait dispose
3. EventFlags wait-any/wait-all + timeout/cancel/dispose
4. Mutex owner/priority inheritance + reentrant 정책
5. SoftwareTimer periodic/one-shot/stop-start race

### 3-2. 검증 명령

빌드:

```bash
dotnet build WpfSamples/WpfSamples.sln
```

전체 회귀:

```bash
dotnet run --project WpfSamples.Tests.Cli/WpfSamples.Tests.Cli.csproj -- --all
```

특정 회귀만 실행:

```bash
dotnet run --project WpfSamples.Tests.Cli/WpfSamples.Tests.Cli.csproj -- --tests MessageQueuePendingSendCanceledByDisposeAsync,MessageQueuePendingReceiveCanceledByDisposeAsync
```

### 3-3. Lab 3 완료 체크리스트

1. 전체 회귀가 통과한다
2. 실패 시 원인 추적 루틴(Message -> 테스트 -> 대상 클래스)을 수행할 수 있다
3. timeout/cancellation/dispose를 명확히 구분해 수정할 수 있다

---

## 부록 A. 단계별 구현 산출물(DoD)

Lab 1 DoD:

1. 최소 스케줄러 API(Start/Stop/Register) 동작
2. 기본 실행/정렬/OneShot 동작
3. 스냅샷 최소 필드 노출

Lab 2 DoD:

1. ShouldYield + quantum 경로 동작
2. 오버런 정책 3종 동작
3. 실행 통계/trace 관찰 가능

Lab 3 DoD:

1. primitive 안정성 회귀 통과
2. 스케줄러 회귀 통과
3. CLI 기반 재검증 루틴 확립

---

## 부록 B. 주니어 디버깅 루틴 (고정 절차)

1. 증상 분류: 실행 안 됨 / 지연 / 선점 실패 / 종료 지연
2. 로그 확인: TraceLog + 오류 이벤트
3. 테스트 매핑: 실패 테스트 메서드 확인
4. 코드 포인트 확인:
   - runnable 선택 문제: CopyRunnableTasks
   - 선점 문제: ShouldYieldToHigherPriorityTask / ExecuteTaskAsync
   - 다음 실행 문제: ScheduleNextRunIfRegistered
   - dispose 경합: primitive Dispose + pending wait 경로
5. 최소 수정 후 같은 테스트 재실행
6. 최종 --all 통과 확인

---

## 마무리

이 3단계 실습을 완료하면, 주니어 개발자도 다음을 수행할 수 있습니다.

1. 별도 프로젝트에서 RTOS 스타일 스케줄러를 직접 구현
2. 협력형 선점 + quantum 근사 선점 적용
3. 수명주기 경합(timeout/cancel/dispose)까지 테스트로 고정
