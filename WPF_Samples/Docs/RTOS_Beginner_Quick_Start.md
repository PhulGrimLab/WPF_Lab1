# RTOS 초보자 30분 빠른 시작

이 문서는 C#, RTOS가 처음인 분이 현재 프로젝트 코드를 직접 따라 보면서
"실행 흐름"과 "선점 시뮬레이터 핵심"을 30분 안에 잡도록 만든 실습 가이드입니다.

이 문서의 목표는 2가지입니다.

1. 코드 위치를 빠르게 찾는다.
2. 왜 이렇게 동작하는지 스스로 설명할 수 있게 만든다.

---

## 시작 전 준비 (1분)

아래 파일을 VS Code에서 미리 열어두면 실습이 빠릅니다.

1. Wpf.Lib.RTOS/SchedulerService.cs
2. Wpf.Lib.RTOS/IScheduledTask.cs
3. Wpf.Lib.RTOS/Tasks/SchedulerMonitorDemo.cs
4. WpfSamples/MainWindowViewModel.cs
5. WpfSamples/MainWindow.xaml
6. Wpf.Lib.RTOS/Tasks/SampleTasks.cs

---

## 1단계 (0~6분): 큰 그림 먼저 잡기

### 1-1. 한 줄 개념

- 태스크가 무엇을 할지는 태스크 코드가 결정
- 태스크를 언제 실행할지는 SchedulerService가 결정

### 1-2. 코드에서 찾을 위치

1. SchedulerMonitorDemo 생성자: 샘플 태스크 등록
2. Start 버튼 커맨드: SchedulerMonitorDemo 시작
3. SnapshotChanged 이벤트: 화면 데이터 갱신

### 1-3. 이해 체크

아래 질문에 답할 수 있으면 통과입니다.

1. Start를 누르면 어떤 객체의 어떤 메서드가 호출되는가?
2. UI 표 데이터는 태스크 객체를 직접 읽는가, 스냅샷을 읽는가?

---

## 2단계 (6~12분): 태스크 규약 3개 이해

### 2-1. IScheduledTask

초보자는 아래 5개만 먼저 기억해도 충분합니다.

1. Name
2. Priority
3. Period
4. Mode (Periodic/OneShot)
5. ExecuteAsync(context, token)

### 2-2. SchedulerTaskBase

왜 필요한가:

1. NextRunAt, IsEnabled 같은 공통 상태를 안전하게 관리
2. 태스크 작성자는 핵심 작업(ExecuteAsync)만 구현 가능

### 2-3. ScheduledTask

람다로 빠르게 태스크를 만들 때 사용합니다.

짧은 예시:

```csharp
var heartbeat = new ScheduledTask(
	name: "Heartbeat",
	priority: Enum_TaskPriority.Normal,
	period: TimeSpan.FromMilliseconds(500),
	mode: Enum_TaskExecutionMode.Periodic,
	executeAsync: (_, _) =>
	{
		Console.WriteLine("tick");
		return Task.CompletedTask;
	},
	statusProvider: () => "alive");
```

---

## 3단계 (12~20분): SchedulerService 핵심 4메서드

아래 순서로 읽으면 가장 쉽습니다.

1. CopyRunnableTasks
2. ExecuteTaskAsync
3. ScheduleNextRunIfRegistered
4. PublishSnapshot

### 3-1. CopyRunnableTasks

무슨 역할인가:

1. 지금 실행 가능한 태스크만 고른다.
2. 우선순위 높은 순으로 정렬한다.

왜 lock을 짧게 쓰나:

- 태스크 속성 접근이 느려도 전체 스케줄러가 오래 잠기지 않게 하기 위해서입니다.

### 3-2. ExecuteTaskAsync

핵심 동작 4개:

1. 시작 통계 기록
2. 실제 태스크 실행
3. 성공/실패 통계 기록
4. finally에서 다음 실행 시각 예약

포인트:

- 실패해도 finally에서 다음 예약 계산이 진행되어 스케줄러 흐름이 무너지지 않습니다.

### 3-3. ScheduleNextRunIfRegistered

정책 요약:

1. OneShot: 실행 후 자동 비활성화
2. Periodic + FixedRate: 원래 시각 기준
3. Periodic + FixedDelay: 완료 시각 기준
4. Periodic + SkipMissedTicks: 밀린 주기 건너뜀

### 3-4. PublishSnapshot

왜 중요한가:

- UI가 내부 mutable 상태를 직접 만지지 않고, 읽기 전용 스냅샷으로 안전하게 표시할 수 있습니다.

---

## 4단계 (20~25분): 선점 데모 핵심 이해

현재 프로젝트 선점 모델은 협력형 선점입니다.

1. 스케줄러가 "더 높은 우선순위 runnable"을 감지
2. 태스크 코드가 context.ShouldYield()를 확인
3. true면 현재 작업을 끊고 return하여 양보

초보자 주의:

- 이것은 인터럽트 기반 강제 선점이 아니라 협력형 양보 모델입니다.

### 표시 계층 스레드 관련 핵심

현재 데모 정책:

1. 선점 판단 대상은 RTOS 태스크(LOW/HIGH)
2. 표시 계층 스레드는 선점 판정 대상에서 제외
3. 화면에서는 현재 실행 TID(주 정보)와 UI TID(참고)를 분리해서 표시

---

## 5단계 (25~30분): 테스트로 고정

열어볼 파일:

1. WpfSamples/Tests/RtosTestRunner.cs
2. Docs/RTOS_Test_Guide.md

먼저 볼 테스트 이름:

1. Scheduler runs higher priority task first
2. Scheduler executes one-shot task once
3. EventFlags wait-all waits for all flags
4. MessageQueue preserves FIFO order

읽는 요령:

1. 테스트 이름으로 의도를 파악
2. Arrange/Act/Assert 순서로 실제 확인값을 본다
3. 문서 설명과 테스트 확인값이 맞는지 비교한다

---

## 바로 해보는 미니 실습

### 실습 A: 태스크 1개 추가

목표:

- ScheduledTask로 Heartbeat를 추가하고 RunCount 변화를 확인

체크:

1. RTOS Monitor에 태스크가 보이는가
2. RunCount가 증가하는가
3. Start TID/Done TID가 기록되는가

### 실습 B: OverrunPolicy 비교

목표:

- 긴 작업 태스크를 만들어 정책 차이를 체감

방법:

1. period 50ms, 실제 실행 120ms 태스크 준비
2. FixedRate와 FixedDelay를 각각 적용
3. NextRunAt, DeadlineMissCount 변화를 비교

### 실습 C: 협력형 양보 확인

목표:

- context.ShouldYield()가 true일 때 LOW가 양보하는지 확인

체크:

1. 양보 횟수 증가
2. HIGH 실행 횟수 증가
3. 로그에 양보 메시지 기록

---

## 자주 막히는 질문 5개

1. OneShot이 왜 다시 실행되지 않나요?

- 실행 후 자동 비활성화되고 NextRunAt이 MaxValue로 설정되기 때문입니다.

2. StopAsync가 늦게 끝나는 이유는 무엇인가요?

- 태스크 내부에서 cancellationToken을 늦게 확인하면 종료가 지연될 수 있습니다.

3. UI TID가 고정인데 정상인가요?

- 정상입니다. WPF UI 스레드는 일반적으로 고정입니다.

4. 그럼 선점 의미가 없나요?

- 아닙니다. 이 데모는 UI 스레드 선점이 아니라 RTOS 태스크 간 우선순위 선행을 보여줍니다.

5. Start TID와 Done TID가 왜 다를 수 있나요?

- async/await 후 다른 워커 스레드로 이어질 수 있기 때문입니다.

---

## 다음 읽기 추천

1. Docs/RTOS_Beginner_Complete_Guide.md
2. Docs/RTOS_Primitives_Deep_Dive.md
3. Docs/RTOS_Real_vs_Simulator_Gap.md

한 문장 요약:

- 빠른 시작은 흐름을 잡는 문서이고, 완전 가이드는 이유와 내부 구조를 깊게 이해하는 문서입니다.
