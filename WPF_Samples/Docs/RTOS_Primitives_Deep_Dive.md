# RTOS Primitive 심화 가이드

이 문서는 Wpf.Lib.RTOS의 동기화 primitive를 비교 중심으로 설명합니다.

대상 독자:

- C# 초보자이지만 기본 문법은 이해한 상태
- Semaphore, Mutex, Event Flags, Message Queue를 언제 써야 할지 헷갈리는 상태

---

## 1. 한눈에 비교

| Primitive | 핵심 목적 | 데이터 전달 | 소유자(owner) 개념 | timeout 지원 | 대표 사용 상황 |
| --- | --- | --- | --- | --- | --- |
| RtosSemaphore | 동시 접근 개수 제한 | 없음 | 없음 | 있음 | 제한된 리소스 풀(예: 슬롯 3개) |
| RtosMutex | 상호 배제(1개만 접근) | 없음 | 있음 | 있음 | 공유 상태를 한 번에 1개 태스크만 수정 |
| RtosEventFlags | 비트 이벤트 신호 동기화 | 비트 플래그(신호) | 없음 | 있음 | 여러 조건이 충족될 때까지 대기 |
| RtosMessageQueue<T> | 태스크 간 데이터 전달 | 있음(T) | 없음 | 있음 | 생산자-소비자 패턴 |
| RtosSoftwareTimer | 시간 기반 콜백 실행 | 콜백 내부에서 처리 | 없음 | 주기/일회 | 주기 신호 발생, watchdog 스타일 |

---

## 2. 무엇을 선택해야 하나

아래 질문 순서로 고르면 됩니다.

1. 데이터 자체를 전달해야 하나?
- 예: 센서값, 명령 객체 전달
- 그렇다면: RtosMessageQueue<T>

2. 데이터는 필요 없고 신호만 필요하나?
- 예: 초기화 완료, 통신 연결됨
- 그렇다면: RtosEventFlags

3. 공유 자원에 동시에 들어가는 개수를 제한해야 하나?
- 예: 동시 작업 4개까지 허용
- 그렇다면: RtosSemaphore

4. 공유 자원은 반드시 한 번에 한 태스크만 접근해야 하나?
- 예: 설정 파일 갱신, 공유 카운터 일관성
- 그렇다면: RtosMutex

5. 시간 기준으로 반복 신호를 만들고 싶나?
- 예: 100ms마다 상태 체크
- 그렇다면: RtosSoftwareTimer

---

## 3. 각 primitive 동작 이해

### 3-1. RtosSemaphore

핵심 아이디어:

- 카운트를 가진 입장권 시스템
- WaitAsync 성공 시 입장권 1장 사용
- Release 시 입장권 1장 반납

짧은 예제:

```csharp
using var semaphore = new RtosSemaphore(initialCount: 2, maxCount: 2);

var ok1 = await semaphore.WaitAsync(TimeSpan.FromMilliseconds(50));
var ok2 = await semaphore.WaitAsync(TimeSpan.FromMilliseconds(50));
var ok3 = await semaphore.WaitAsync(TimeSpan.FromMilliseconds(50)); // false 가능

if (ok1) semaphore.Release();
if (ok2) semaphore.Release();
```

실수 포인트:

- Wait 성공 후 Release를 빼먹으면 자원 고갈처럼 보임
- 이미 가득 찬 상태에서 Release하면 예외 발생 가능

---

### 3-2. RtosMutex

핵심 아이디어:

- 한 번에 하나의 owner만 잠금을 잡는다
- 다른 owner가 Release하면 예외
- 대기 중 우선순위를 계산해 EffectiveOwnerPriority 제공

짧은 예제:

```csharp
using var mutex = new RtosMutex();

var acquired = await mutex.WaitAsync("TaskA", Enum_TaskPriority.Normal, TimeSpan.FromMilliseconds(100));
if (acquired)
{
    try
    {
        // 공유 상태 수정
    }
    finally
    {
        mutex.Release("TaskA");
    }
}
```

실수 포인트:

- owner 문자열을 다르게 넣으면 Release에서 실패
- finally 없이 return하면 잠금 해제가 누락됨

---

### 3-3. RtosEventFlags

핵심 아이디어:

- uint 비트를 이벤트 신호로 사용
- WaitAnyAsync: 지정 비트 중 하나라도 set되면 완료
- WaitAllAsync: 지정 비트가 모두 set되어야 완료

짧은 예제:

```csharp
using var flags = new RtosEventFlags();

var waitTask = flags.WaitAllAsync(0b0011, autoClear: true, TimeSpan.FromSeconds(1));

flags.Set(0b0001);
flags.Set(0b0010);

var matched = await waitTask; // 0b0011
```

현재 구현에서 예외 규칙:

- timeout: TimeoutException
- 외부 cancellation: OperationCanceledException
- dispose 중/후 대기: ObjectDisposedException

실수 포인트:

- autoClear 의미를 반대로 이해하면 디버깅이 어려움
- wait mask를 0으로 주면 예외

---

### 3-4. RtosMessageQueue<T>

핵심 아이디어:

- 생산자가 SendAsync로 넣고, 소비자가 ReceiveAsync로 꺼낸다
- 큐가 가득 차거나 비어 있으면 timeout/cancellation로 제어
- FIFO 순서 유지

짧은 예제:

```csharp
using var queue = new RtosMessageQueue<string>(capacity: 2);

await queue.SendAsync("A", TimeSpan.FromMilliseconds(100));
await queue.SendAsync("B", TimeSpan.FromMilliseconds(100));

var r1 = await queue.ReceiveAsync(TimeSpan.FromMilliseconds(100)); // A
var r2 = await queue.ReceiveAsync(TimeSpan.FromMilliseconds(100)); // B
```

실수 포인트:

- capacity를 너무 작게 잡으면 timeout이 잦아짐
- timeout을 짧게 주면 정상 상황도 실패처럼 보일 수 있음

---

### 3-5. RtosSoftwareTimer

핵심 아이디어:

- one-shot 또는 periodic 콜백 실행
- periodic 모드에서는 콜백 예외가 나도 다음 주기 계속 진행
- TimerError 이벤트로 예외를 보고

짧은 예제:

```csharp
await using var timer = new RtosSoftwareTimer(
    TimeSpan.FromMilliseconds(100),
    isPeriodic: true,
    _ =>
    {
        // 주기 작업
        return Task.CompletedTask;
    });

timer.TimerError += (_, ex) => Console.WriteLine(ex.Message);

timer.Start();
await Task.Delay(1000);
await timer.StopAsync();
```

실수 포인트:

- 콜백에서 오래 걸리는 작업을 하면 주기가 밀림
- StopAsync 없이 앱 종료 로직만 믿으면 종료 타이밍이 불명확해짐

---

## 4. 조합 패턴 예제

### 4-1. Queue + Semaphore

용도:

- 큐에 들어온 작업을 worker 수 제한으로 처리

패턴:

1. 소비자가 queue.ReceiveAsync로 메시지 획득
2. semaphore.WaitAsync로 worker 슬롯 획득
3. 처리 완료 후 semaphore.Release

### 4-2. EventFlags + MessageQueue

용도:

- 데이터는 queue로 받고, 상태 전이는 flags로 제어

예:

- bit0: 연결 완료
- bit1: 인증 완료
- bit2: 데이터 수신 가능

WaitAll(0b0111) 후 본 처리 시작

### 4-3. Mutex + Timer

용도:

- 주기적 상태 갱신 중 공유 상태 보호

패턴:

- timer 콜백에서 mutex 획득 후 상태 수정
- UI 스레드도 동일 mutex 규칙으로 읽기

---

## 5. 장애/디버깅 체크리스트

1. timeout이 반복된다면
- timeout 값이 실제 처리 시간보다 너무 짧지 않은지 확인
- queue capacity가 너무 작은지 확인
- 주기 작업이 과도하게 길지 않은지 확인

2. Stop이 늦다면
- 태스크가 cancellationToken을 실제로 사용하는지 확인
- Task.Delay에는 cancellationToken을 전달했는지 확인

3. 상태가 꼬인다면
- 공유 상태 접근이 mutex/semaphore 규칙을 지키는지 확인
- owner 문자열이 일관된지 확인

4. 이벤트가 안 깨어난다면
- EventFlags mask와 Set 비트가 맞는지 확인
- autoClear가 의도와 맞는지 확인

---

## 6. 초보자 실습 과제

### 과제 A. 생산자-소비자 만들기

목표:

- Queue로 int 메시지를 전달하고 처리 결과를 로그에 남기기

요구:

1. 생산자 태스크: 100ms마다 숫자 증가 후 SendAsync
2. 소비자 태스크: ReceiveAsync 후 50ms 처리
3. 10개 처리 후 OneShot 태스크로 완료 로그

### 과제 B. EventFlags로 단계 게이트 만들기

목표:

- 초기화 단계를 bit flag로 표현

요구:

1. bit0 설정 태스크(설정 로드)
2. bit1 설정 태스크(통신 준비)
3. bit2 설정 태스크(DB 준비)
4. WaitAll(0b0111) 후 메인 태스크 시작

### 과제 C. Mutex 경쟁 실험

목표:

- 두 태스크가 같은 상태를 갱신할 때 mutex 효과 확인

요구:

1. mutex 없이 1회 실행
2. mutex 적용 후 1회 실행
3. 결과 차이를 스냅샷/로그로 비교

---

## 7. 마무리

primitive를 외우려고 하기보다, 문제를 먼저 분류하면 선택이 쉬워집니다.

- 데이터 전달: Queue
- 신호 동기화: EventFlags
- 개수 제한: Semaphore
- 단일 소유 잠금: Mutex
- 시간 트리거: SoftwareTimer

---

## 8. 코드로 바로 이동하기

문서를 읽다가 실제 구현을 바로 보고 싶다면 아래 순서로 열어보세요.

### 8-1. Primitive 구현 파일

1. `Wpf.Lib.RTOS/RtosSemaphore.cs`
2. `Wpf.Lib.RTOS/RtosMutex.cs`
3. `Wpf.Lib.RTOS/RtosEventFlags.cs`
4. `Wpf.Lib.RTOS/RtosMessageQueue.cs`
5. `Wpf.Lib.RTOS/RtosSoftwareTimer.cs`
6. `Wpf.Lib.RTOS/RtosTickCounter.cs`

### 8-2. 스케줄러 연동 핵심 파일

1. `Wpf.Lib.RTOS/IScheduledTask.cs`
2. `Wpf.Lib.RTOS/SchedulerTaskBase.cs`
3. `Wpf.Lib.RTOS/SchedulerService.cs`
4. `Wpf.Lib.RTOS/TaskRuntimeInfo.cs`
5. `Wpf.Lib.RTOS/ScheduledTaskSnapshot.cs`

### 8-3. 동작 확인용 테스트 파일

1. `WpfSamples/Tests/RtosTestRunner.cs`
2. `WpfSamples/Tests/RtosAssert.cs`
3. `WpfSamples/Tests/RtosTestResult.cs`

### 8-4. UI에서 실제 연결 확인 파일

1. `WpfSamples/MainWindowViewModel.cs`
2. `WpfSamples/Samples_RTOS/CounterTask.cs`
3. `WpfSamples/Samples_RTOS/ClockTask.cs`
4. `WpfSamples/Samples_RTOS/UiRefreshTask.cs`

### 8-5. 추천 읽기 루트

처음 한 번은 아래 순서로 읽어보면 이해가 빠릅니다.

1. `RtosSemaphore.cs`와 `RtosMutex.cs`를 먼저 읽고 잠금 차이를 이해
2. `RtosEventFlags.cs`와 `RtosMessageQueue.cs`를 읽고 신호/데이터 전달 차이 이해
3. `RtosSoftwareTimer.cs`를 읽고 시간 기반 트리거 이해
4. `SchedulerService.cs`에서 실제 실행 루프와 primitive 사용 맥락 연결
5. `RtosTestRunner.cs`에서 검증 시나리오를 보며 동작 재확인

---

## 9. 성숙도 평가 문서

현재 구현이 RTOS 시뮬레이터로서 어느 수준인지 판단하려면 아래 문서를 함께 보세요.

- Docs/RTOS_Simulator_Maturity_Checklist.md

이 다섯 가지를 조합하면 대부분의 RTOS 스타일 동기화 시나리오를 표현할 수 있습니다.

---

## 10. 2026-05-22 실전 보강 메모

최근 선점 데모 개선을 반영해, primitive를 읽을 때 아래 기준을 추가로 기억하면 좋습니다.

### 10-1. UI 스레드는 동기화 판정 대상이 아니다

이 프로젝트에서 primitive 동기화는 RTOS 태스크 간 협력 동작을 표현하기 위한 것입니다.

즉,

1. UI 스레드는 화면 표시 경계
2. primitive 판정/선점 해석은 RTOS 태스크 경계

로 분리해서 봐야 혼동이 줄어듭니다.

### 10-2. 협력형 선점과 primitive의 관계

`ShouldYield()`는 "양보 타이밍"을 알려주는 신호이고,
실제 데이터/상태 동기화는 primitive가 담당합니다.

예:

1. LOW가 길게 실행 중 `ShouldYield()` 감지
2. LOW가 안전 지점에서 return
3. HIGH가 실행되어 Queue/EventFlags 처리

여기서 Queue/EventFlags/Mutex가 없으면 상태 일관성이 쉽게 깨집니다.

### 10-3. 초보자 관찰 지표 4개

실습 중 아래 4개를 동시에 보면 학습 효과가 큽니다.

1. 로그: 양보/실행 이벤트 순서
2. 카운터: 실행 횟수, 양보 횟수
3. TID: LOW/HIGH 마지막 실행 스레드
4. timeout/cancel 예외: primitive 실패 원인

### 10-4. 문서 간 연결

1. primitive 선택: 이 문서
2. 실행 루프/선점: `Docs/SchedulerService_Flow.md`
3. 테스트 검증: `Docs/RTOS_Test_Guide.md`

이 3개를 묶어 보면 "개념-코드-검증"이 한 번에 연결됩니다.
