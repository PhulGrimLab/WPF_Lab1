# Wpf.Lib.RTOS 주니어 코드 구조/수정 가이드

이 문서는 C# 주니어 개발자가 현재 RTOS 라이브러리의 구조와 실행 흐름을 이해하고,
안전하게 직접 수정/보완할 수 있도록 만든 실무형 설명서입니다.

이 가이드의 목표는 3가지입니다.

1. 어디를 보면 동작이 바뀌는지 빠르게 찾는다.
2. 어떤 순서로 읽어야 실수 없이 이해되는지 안다.
3. 수정 후 어떤 테스트로 검증해야 하는지 바로 실행할 수 있다.

---

## 1. 먼저 결론: 이 라이브러리를 어떻게 바라보면 좋은가

현재 구현은 "실시간 커널"이 아니라 "응용프로그램용 작업 오케스트레이터"입니다.

- 강점: 다수 작업의 주기 실행, 우선순위, 상태 스냅샷, 일관된 중지/해제 흐름
- 한계: 하드 실시간 보장은 불가, 협력형 양보(ShouldYield)와 time quantum 근사 모델

따라서 이 코드는 "여러 내부 작업을 정책적으로 통제"하는 계층으로 사용하면 가장 효과적입니다.

---

## 2. 파일 맵: 수정 포인트를 빨리 찾는 지도

핵심 파일(라이브러리):

1. Wpf.Lib.RTOS/SchedulerService.cs
- 스케줄러 메인 엔진
- 실행 후보 선택, 정렬, 실행, 다음 시각 계산, 스냅샷 발행

2. Wpf.Lib.RTOS/IScheduledTask.cs
- 태스크 계약(인터페이스)

3. Wpf.Lib.RTOS/SchedulerTaskBase.cs
- 태스크 공통 구현(NextRunAt/IsEnabled lock 보호)

4. Wpf.Lib.RTOS/SchedulerContext.cs
- 태스크 실행 컨텍스트(now, ShouldYield, MarkPreempted)

5. Wpf.Lib.RTOS/TaskRuntimeInfo.cs
- 실행 통계/상태 기록

6. Wpf.Lib.RTOS/RtosSemaphore.cs
7. Wpf.Lib.RTOS/RtosMutex.cs
8. Wpf.Lib.RTOS/RtosEventFlags.cs
9. Wpf.Lib.RTOS/RtosMessageQueue.cs
10. Wpf.Lib.RTOS/RtosSoftwareTimer.cs
- 동기화 primitive 구현

검증/사용 파일(샘플 + 테스트):

1. WpfSamples/MainWindowViewModel.cs
- 스케줄러 연결, 스냅샷 UI 반영, 데모 실행 흐름

2. WpfSamples/Tests/RtosTestRunner.cs
- 전체 회귀 테스트 정의

3. WpfSamples.Tests.Cli/Program.cs
- CLI 테스트 러너(--all, --compare-quantum, --tests)

---

## 3. 읽기 순서: 주니어가 가장 덜 헷갈리는 경로

아래 순서를 권장합니다.

1. IScheduledTask.cs
- 태스크가 무엇을 제공해야 하는지 먼저 고정

2. SchedulerTaskBase.cs
- 인터페이스 구현 부담을 어떻게 줄였는지 이해

3. SchedulerContext.cs
- 태스크가 실행 중 어떤 신호를 받을 수 있는지 파악

4. SchedulerService.cs (아래 4개 메서드 중심)
- RunAsync
- CopyRunnableTasks
- ExecuteTaskAsync
- ScheduleNextRunIfRegistered

5. RtosTestRunner.cs
- 실제 기대 동작을 테스트 이름으로 역추적

이 순서로 보면 "계약 -> 실행 -> 검증" 흐름이 맞물려 이해됩니다.

---

## 4. SchedulerService 핵심 흐름 (코드 기준)

### 4-1. Start/Stop

- Start()
  - 상태를 Running으로 전환
  - Task.Run으로 RunAsync 루프 시작

- StopAsync(timeout)
  - _cts.Cancel()로 종료 요청
  - runTask 종료를 timeout 내 대기
  - 무한 대기를 실행 스레드 내부에서 호출하는 경우 false 반환(자기 교착 보호)

### 4-2. RunAsync 메인 루프

루프 1회는 아래 순서입니다.

1. 현재 시각(now) 획득
2. CopyRunnableTasks(now, _executionBuffer)
3. 실행 후보를 foreach로 ExecuteTaskAsync
4. PublishSnapshotIfDue(now)
5. GetLoopDelay(now)만큼 대기

### 4-3. CopyRunnableTasks

이 메서드가 "무엇을 지금 실행할지"를 결정합니다.

- 내부 due queue(PriorityQueue)에서 now 이전 태스크를 꺼냄
- stale priority(변경된 NextRunAt/Priority)면 재큐잉
- disabled 태스크는 즉시 실행하지 않고 revisit 시간만 내부 관리
- 최종 실행 후보는 CompareRunnableTasks로 정렬

정렬 정책(현재 구현):

- 예정 시각 차이가 tick보다 크면 due-time 우선
- 차이가 작으면 priority 우선

### 4-4. ExecuteTaskAsync

한 태스크 실행의 표준 수명주기입니다.

1. 실행 시작 마킹(MarkTaskStarted)
2. 필요 시 quantum 토큰 생성(timeQuantum)
3. task.ExecuteAsync(context, token) 호출
4. 성공/선점/실패별 통계 업데이트
5. ScheduleNextRunIfRegistered로 다음 실행 시각 반영

주의할 점:

- quantum 취소는 "스케줄러 중지 취소"와 구분해서 처리
- 선점된 경우 MarkPreempted 처리 후 즉시 재스케줄

### 4-5. ScheduleNextRunIfRegistered

다음 실행 시각 정책의 중심입니다.

- OneShot: 비활성화 + MaxValue
- Periodic:
  - preempted면 completedAt으로 즉시 재실행 예약
  - 아니면 OverrunPolicy(FixedRate/FixedDelay/SkipMissedTicks) 적용

---

## 5. Primitive 수정 시 꼭 지켜야 할 규칙

### 5-1. Dispose는 "대기 중 작업" 관점에서 설계

단순히 내부 동기화 객체를 즉시 Dispose하면,
이미 진행 중인 WaitAsync와 경합하여 hang/timeout이 발생할 수 있습니다.

현재 MessageQueue는 이 이슈를 기준으로 정리돼 있습니다.

- Dispose에서 종료 상태 설정
- shutdown cancellation을 먼저 전달
- 대기 작업이 정상적으로 빠지도록 경로를 보장

### 5-2. timeout, cancel, dispose를 분리

예외 의미를 섞지 말아야 테스트와 운영 로그 해석이 쉬워집니다.

- timeout: false 반환 또는 TimeoutException(타입 정책에 따름)
- 외부 cancellation: OperationCanceledException
- dispose: ObjectDisposedException

### 5-3. lock 영역 최소화

I/O 대기나 await를 lock 내부에서 하지 않습니다.

- lock에서는 상태 읽기/쓰기만
- 실제 대기는 lock 외부에서 수행

---

## 6. 태스크를 새로 추가하는 표준 절차

1. 태스크 클래스 작성
- SchedulerTaskBase 상속 권장
- ExecuteAsync에서 cancellationToken 체크
- 긴 루프면 context.ShouldYield() 체크 지점 추가

2. 등록
- MainWindowViewModel 또는 조합 계층에서 Register

3. UI 노출 필요 시
- SnapshotChanged 데이터 바인딩 경로 확인

4. 테스트 추가
- RtosTestRunner.cs에 테스트 메서드 + 목록 등록

5. CLI로 빠른 검증
- 특정 테스트만 실행: --tests
- 전체 회귀: --all

---

## 7. 주니어용 수정 체크리스트 (실수 방지)

코드 수정 전:

1. 변경하려는 동작을 테스트 이름으로 찾았는가
2. 영향을 받는 메서드가 1-hop 범위로 식별됐는가
3. timeout/cancel/dispose 의미를 구분했는가

코드 수정 후:

1. 최소 범위 테스트를 먼저 실행했는가
2. 전체 회귀를 다시 실행했는가
3. 문서/진행기록을 갱신했는가

---

## 8. 자주 하는 실수와 바로잡는 방법

1. 실수: Periodic 태스크에서 cancellation 체크 누락
- 증상: StopAsync가 늦게 끝남
- 대응: 작업 루프 중간마다 cancellationToken.ThrowIfCancellationRequested 또는 IsCancellationRequested 확인

2. 실수: 공유 상태를 lock 없이 갱신
- 증상: 스냅샷 값이 순간적으로 불일치
- 대응: 상태 소유 lock 기준을 파일 단위로 명시하고 준수

3. 실수: dispose 직후 예외 타입이 케이스마다 달라짐
- 증상: 테스트가 간헐 실패
- 대응: dispose 경로는 ObjectDisposedException으로 수렴하도록 catch/throw 경로 정리

4. 실수: 우선순위 정책 변경 후 정렬만 수정
- 증상: due queue stale 판정과 불일치
- 대응: CompareRunnableTasks와 queue priority 생성/검증 로직을 함께 검토

5. 실수: 선점 데모 로그를 건별로 과다 출력
- 증상: 데모 탭 UI가 간헐적으로 끊기거나 프레임이 떨어짐
- 대응: 이벤트 건별 출력 대신 누적 N회 요약 출력(예: 3회/5회마다 1회) 적용

6. 실수: ActiveTasks를 snapshot마다 무조건 Clear/Add
- 증상: 상태가 거의 안 바뀌어도 UI 재구성 비용이 반복됨
- 대응: 활성 task 집합이 바뀐 경우에만 RebuildActiveTasks 실행

7. 실수: 기아(starvation) 판정 임계값을 고정값으로만 운영
- 증상: 환경에 따라 경고가 과민/둔감하게 표시됨
- 대응: 선점 데모의 "기아 감지 민감도" 슬라이더로 임계값(초)을 조정

---

## 8-1. 선점 데모 튜닝 가이드 (로그/기아/UI)

최근 데모 안정화에서 실제로 효과가 있었던 조합입니다.

1. 로그 정책
- 원칙: 중요 이벤트만 출력, 반복 이벤트는 누적 요약
- 구현: QueuePreemptionLogEvery(key, every, message)
- 권장: NORMAL 5회, HIGH 3회, LOW 양보 4회, LOW 완료 2회 주기
- 추가: 선점 데모 하단 로그는 1초 간격으로 누적 요약 1건만 UI에 반영

2. UI 갱신 정책
- 원칙: 값이 바뀐 항목만 갱신
- 구현: ActiveTasks 집합 변경이 없으면 RebuildActiveTasks 생략

3. 기아 경고 판정
- 판정 근거: LOW RunCount 증가 정체 + LOW LastStartedAt 정체 + 상위 우선순위 진행 지속
- 표시: 선점 데모 패널의 "기아 감지" 라인

4. 기아 민감도 슬라이더
- 항목: 기아 판정 지연(초)
- 의미: LOW 정체를 몇 초 이상 지속하면 주의로 볼지 결정
- 기본값: 3.0초
- 권장 범위:
  - 1.5~2.5초: 빠른 실험/디버깅(민감)
  - 2.5~4.0초: 일반 데모/검증(균형)
  - 4.0초 이상: 장기 추세 관찰(둔감)

운영 팁:

1. HIGH burst를 늘렸다면 민감도도 함께 높여 오탐을 줄인다.
2. LOW 작업 단위를 더 잘게 쪼갰다면 민감도를 낮춰도 안정적이다.
3. 기아 경고가 자주 뜨면 먼저 로그량/quantum/LOW yield 지점을 함께 점검한다.

UI 병목 점검 메모:

1. 선점 데모에서 가장 큰 체감 병목은 ListBox 로그 항목의 잦은 Insert 갱신이다.
2. 그래서 로그는 건별 append보다 "누적 후 주기 요약"이 효과가 크다.
3. 상태 카드/카운터는 SetProperty 비교로 불필요한 PropertyChanged를 줄이고 있으므로,
   1차 최적화 우선순위는 로그 경로가 맞다.

---

## 9. 실제 실행/검증 명령

빌드:

```bash
dotnet build WpfSamples/WpfSamples.sln
```

전체 테스트:

```bash
dotnet run --project WpfSamples.Tests.Cli/WpfSamples.Tests.Cli.csproj -- --all
```

특정 테스트만 실행(예: MessageQueue dispose 회귀):

```bash
dotnet run --project WpfSamples.Tests.Cli/WpfSamples.Tests.Cli.csproj -- --tests MessageQueuePendingSendCanceledByDisposeAsync,MessageQueuePendingReceiveCanceledByDisposeAsync
```

quantum 비교:

```bash
dotnet run --project WpfSamples.Tests.Cli/WpfSamples.Tests.Cli.csproj -- --compare-quantum
```

---

## 10. 권장 학습 루프 (1~2주)

1주차:

1. SchedulerService 핵심 4메서드(Run/Copy/Execute/Schedule)만 집중
2. RtosTestRunner의 Scheduler 관련 테스트를 한 줄씩 따라가며 매칭
3. 샘플 태스크 1개를 추가하고 실행 흐름 관찰

2주차:

1. Primitive 1개 선택(MessageQueue 권장)하여 timeout/cancel/dispose 경로 추적
2. 테스트 1개를 먼저 실패시키는 수정 후 다시 통과시키는 연습
3. 문서와 코드의 불일치 1개 이상 찾아 보완

이 루프를 1회 수행하면, 단순 사용 수준을 넘어 "수정 가능한 유지보수자" 단계로 올라갈 수 있습니다.

---

## 11. 관련 문서

1. Docs/RTOS_Beginner_Quick_Start.md
2. Docs/RTOS_Beginner_Complete_Guide.md
3. Docs/RTOS_Test_Guide.md
4. Docs/SchedulerService_Flow.md
5. Docs/RTOS_Primitives_Deep_Dive.md
