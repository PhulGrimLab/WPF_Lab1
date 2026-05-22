# RTOS 초보자 30분 빠른 시작

이 문서는 C# 초보자가 Wpf.Lib.RTOS 코드를 실제로 열어보며 30분 안에 구조를 잡도록 만든 실습 가이드입니다.

---

## 1. 0~5분: 전체 흐름 먼저 잡기

열어볼 파일:
1. Wpf.Lib.RTOS/SchedulerService.cs
2. WpfSamples/MainWindowViewModel.cs

확인할 것:
1. MainWindowViewModel에서 Register -> Start 호출
2. SchedulerService에서 RunAsync 루프가 태스크를 실행
3. SnapshotChanged가 UI 갱신으로 연결

핵심 문장:
- 태스크가 "무엇"을 할지는 태스크 클래스가 결정하고,
- 태스크를 "언제" 실행할지는 SchedulerService가 결정합니다.

---

## 2. 5~12분: 태스크 규약 이해

열어볼 파일:
1. Wpf.Lib.RTOS/IScheduledTask.cs
2. Wpf.Lib.RTOS/SchedulerTaskBase.cs
3. Wpf.Lib.RTOS/ScheduledTask.cs

확인할 것:
1. IScheduledTask의 필수 속성/메서드
2. SchedulerTaskBase의 NextRunAt, IsEnabled 보호 방식(lock)
3. ScheduledTask의 람다 기반 태스크 생성 방식

헷갈리면 이렇게 기억:
- IScheduledTask: 약속(인터페이스)
- SchedulerTaskBase: 기본 구현(편의)
- ScheduledTask: 빠른 생성(람다)

---

## 3. 12~20분: 스케줄링 핵심 4메서드

열어볼 파일:
1. Wpf.Lib.RTOS/SchedulerService.cs

순서대로 볼 메서드:
1. CopyRunnableTasks
2. ExecuteTaskAsync
3. ScheduleNextRunIfRegistered
4. PublishSnapshot

각 메서드 한 줄 요약:
1. 지금 실행 가능한 태스크만 고른다.
2. 실제 태스크를 실행하고 성공/실패 통계를 남긴다.
3. 다음 실행 시각을 정책에 따라 계산한다.
4. UI가 읽을 스냅샷을 만든다.

---

## 4. 20~26분: 동기화 primitive 하나씩 맛보기

추천 순서:
1. RtosSemaphore.cs
2. RtosMutex.cs
3. RtosEventFlags.cs
4. RtosMessageQueue.cs

한 줄 사용 목적:
1. Semaphore: 동시 접근 개수 제한
2. Mutex: owner 기반 단일 잠금
3. EventFlags: 비트 신호 대기
4. MessageQueue: 태스크 간 데이터 전달

---

## 5. 26~30분: 테스트로 이해 고정

열어볼 파일:
1. WpfSamples/Tests/RtosTestRunner.cs
2. Docs/RTOS_Test_Guide.md

추천 확인 항목:
1. Scheduler runs higher priority task first
2. Scheduler executes one-shot task once
3. EventFlags wait-all waits for all flags
4. MessageQueue preserves FIFO order

핵심 문장:
- 문서만 읽지 말고, 테스트 이름과 구현을 같이 보면 이해 속도가 가장 빠릅니다.

---

## 자주 막히는 포인트 3개

1. 왜 OneShot이 다시 실행되지 않나요?
- 실행 후 SetEnabled(false), NextRunAt=MaxValue로 바뀌기 때문입니다.

2. 왜 선점이 즉시 강제되지 않나요?
- 현재는 협력형 모델이라 ShouldYield를 태스크 코드가 확인해야 양보가 일어납니다.

3. 왜 UI를 태스크에서 직접 만지면 안 되나요?
- 스케줄러는 백그라운드 스레드에서 실행되고, WPF UI는 Dispatcher(메인 스레드)에서만 안전하기 때문입니다.

---

## 다음 읽기 추천

1. Docs/RTOS_Beginner_Complete_Guide.md
2. Docs/RTOS_Primitives_Deep_Dive.md
3. Docs/RTOS_Real_vs_Simulator_Gap.md
