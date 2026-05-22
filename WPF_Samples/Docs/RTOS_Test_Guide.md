# RTOS 테스트 실행 가이드 (초보자 실습형)

이 문서는 C# 초보자가 "테스트를 실행하고 결과를 해석"하는 데 집중한 실습 가이드입니다.

이 프로젝트의 테스트는 xUnit/NUnit 기반이 아니라, WPF 앱 안에서 동작하는 자체 러너(`RtosTestRunner`)입니다.

---

## 1. 테스트 구조 한눈에 보기

테스트 관련 파일:

```text
WpfSamples
 └─ Tests
    ├─ RtosAssert.cs        // 단정 헬퍼
    ├─ RtosTestResult.cs    // 결과 모델(PASS/FAIL, 시간, 메시지)
    └─ RtosTestRunner.cs    // 테스트 목록/실행 엔진
```

핵심 개념:

1. `RunAllAsync()`가 테스트 목록을 순서대로 실행
2. 예외가 없으면 PASS
3. 예외가 발생하면 FAIL + 예외 메시지 기록

---

## 2. 테스트 실행 방법 (UI)

1. `WpfSamples` 실행
2. 상단 탭에서 `RTOS Tests` 선택
3. `Run Tests` 버튼 클릭
4. DataGrid 결과 확인

결과 컬럼 의미:

| 컬럼 | 의미 |
| --- | --- |
| `Test` | 테스트 이름 |
| `Result` | PASS / FAIL |
| `Duration` | 실행 시간 |
| `Message` | 성공 메시지(OK) 또는 실패 원인 |

초보자 팁:

1. FAIL이 나오면 `Message`를 먼저 읽고
2. 해당 테스트 이름을 `RtosTestRunner.cs`에서 검색해 구현을 확인하세요.

---

## 3. 현재 테스트 목록 요약 (실제 코드 기준)

아래 그룹은 `RtosTestRunner.RunAllAsync()` 기준으로 정리했습니다.

### 3-1. Semaphore

1. Semaphore wait/release
2. Semaphore rejects over-release
3. Semaphore wait observes cancellation
4. Semaphore throws after dispose

무엇을 보장하나:

1. 기본 획득/반환
2. 과도한 Release 방지
3. cancellation 관찰
4. dispose 후 접근 차단

### 3-2. MessageQueue

1. MessageQueue send/receive
2. MessageQueue preserves FIFO order
3. MessageQueue receive timeout
4. MessageQueue throws after dispose

무엇을 보장하나:

1. 송수신 기본 동작
2. FIFO 순서
3. 빈 큐 timeout
4. dispose 안정성

### 3-3. EventFlags

1. EventFlags wait-any completes when flag is set
2. EventFlags wait-all waits for all flags
3. EventFlags wait observes timeout
4. EventFlags wait observes cancellation
5. EventFlags dispose cancels pending wait

무엇을 보장하나:

1. WaitAny/WaitAll 의미 분리
2. timeout/cancel/dispose 시나리오 분리

### 3-4. Mutex

1. Mutex tracks owner and priority inheritance
2. Mutex throws after dispose

무엇을 보장하나:

1. owner 추적
2. 대기 우선순위 기반 effective priority 반영
3. dispose 안정성

### 3-5. SoftwareTimer

1. SoftwareTimer one-shot fires once
2. SoftwareTimer periodic fires repeatedly
3. SoftwareTimer periodic survives callback errors
4. SoftwareTimer dispose does not throw while callback runs

무엇을 보장하나:

1. one-shot/periodic 동작
2. periodic 콜백 오류 복원력
3. dispose 시 예외 안전성

### 3-6. TickCounter

1. TickCounter converts time to ticks
2. TickCounter rejects overflow tick

무엇을 보장하나:

1. 시간 -> tick 변환 정확성
2. overflow 방어

### 3-7. Scheduler 핵심

1. Scheduler executes periodic task
2. Scheduler runs higher priority task first
3. Scheduler cooperative preemption yields to higher priority
4. Scheduler executes one-shot task once
5. Scheduler stop timeout returns false
6. Scheduler self stop with infinite timeout returns false
7. Scheduler stop honors cancellation
8. Scheduler unregister prevents future execution
9. Scheduler clear removes all tasks
10. Scheduler reports task errors
11. Scheduler survives snapshot handler errors
12. Scheduler records trace entries
13. Scheduler snapshot contains runtime statistics
14. Scheduler overrun task does not overlap
15. Scheduler fixed-delay overrun waits after completion
16. Scheduler skip-missed overrun advances schedule
17. Scheduler snapshot contains task state
18. Scheduler snapshot survives faulty task properties

무엇을 보장하나:

1. 기본 실행/우선순위/OneShot
2. 협력형 선점(ShouldYield)
3. 정지/정리/오류 내성
4. 오버런 정책
5. 스냅샷/트레이스 안정성

---

## 4. 초보자 추천 학습 순서 (테스트 중심)

아래 순서대로 보면 이해가 빠릅니다.

1. Scheduler executes periodic task
2. Scheduler runs higher priority task first
3. Scheduler cooperative preemption yields to higher priority
4. Scheduler executes one-shot task once
5. Scheduler fixed-delay overrun waits after completion
6. Scheduler skip-missed overrun advances schedule
7. EventFlags wait-all waits for all flags
8. MessageQueue preserves FIFO order
9. Mutex tracks owner and priority inheritance

읽는 방법:

1. 테스트 이름으로 의도를 먼저 말로 설명
2. Arrange/Act/Assert 흐름으로 코드 확인
3. 라이브러리 코드(`Wpf.Lib.RTOS`)로 이동해 같은 흐름 추적

---

## 5. FAIL이 났을 때 빠른 디버깅 순서

1. UI `Message` 컬럼의 예외 메시지 확인
2. 테스트 이름으로 `RtosTestRunner.cs` 검색
3. 해당 테스트가 어떤 클래스를 사용하는지 확인
4. 클래스의 dispose/timeout/cancellation 처리부터 점검
5. 마지막으로 SchedulerSnapshot/TraceLog를 확인

초보자 체크리스트:

1. timeout과 cancellation을 같은 것으로 처리하지 않았는가?
2. dispose 후 접근이 허용되고 있지 않은가?
3. Periodic/OneShot 분기가 뒤집히지 않았는가?
4. lock 범위를 너무 길게 잡아 교착 가능성이 생기지 않았는가?

---

## 6. 새 테스트 추가 방법 (안전한 패턴)

### 6-1. 테스트 메서드 작성

```csharp
private static Task MyNewTestAsync()
{
    RtosAssert.True(1 + 1 == 2, "기본 산술 테스트");
    return Task.CompletedTask;
}
```

### 6-2. RunAllAsync 배열에 등록

```csharp
var tests = new (string Name, Func<Task> Execute)[]
{
    ("My new test", MyNewTestAsync),
};
```

네이밍 팁:

1. "대상 + 기대 결과" 형태로 이름 작성
2. 예: `Scheduler stop timeout returns false`

---

## 7. 선점 테스트 읽을 때 주의할 점

현재 프로젝트의 선점 모델은 협력형 선점입니다.

1. 스케줄러가 더 높은 우선순위 runnable 상태를 감지
2. 실행 중 태스크가 `context.ShouldYield()`를 보고 자발적으로 양보

중요:

1. 이는 인터럽트 기반 강제 선점이 아님
2. UI 스레드는 선점 판정 대상이 아님
3. 선점 판정은 RTOS 태스크 간 우선순위 모델 검증 목적

---

## 8. 마무리 요약

한 문장으로 정리하면:

- 이 테스트 세트는 "기능 동작 여부"만이 아니라, timeout/cancellation/dispose/overrun/오류 복원력까지 함께 검증하는 안정성 테스트 묶음입니다.

다음 문서:

1. Docs/RTOS_Beginner_Complete_Guide.md
2. Docs/RTOS_Real_vs_Simulator_Gap.md
