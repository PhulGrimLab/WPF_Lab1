# RTOS 시뮬레이터 성숙도 체크리스트

이 문서는 현재 `Wpf.Lib.RTOS`가 RTOS 시뮬레이터로서 어느 수준인지 빠르게 판단하기 위한 체크리스트입니다.

평가 기준:

- 충족: 기능이 코드에 명확히 구현됨
- 부분 충족: 개념은 있으나 엄밀성/완성도가 부족함
- 미충족: 현재 코드 범위에 없음

---

## 1. 현재 성숙도 요약

현재 단계는 다음으로 판단할 수 있습니다.

- 단계: 학습/데모용 RTOS 스타일 시뮬레이터
- 특징: 태스크 주기 실행, 우선순위 정렬, 동기화 primitive, 스냅샷/로그 제공
- 한계: 하드 실시간 보장, 선점형 커널 동작, ISR/문맥교환 모델 부재

---

## 2. 체크리스트

| 영역 | 항목 | 현재 상태 | 비고 |
| --- | --- | --- | --- |
| 스케줄링 | 주기 실행 루프 | 충족 | tickInterval 기반 반복 실행 |
| 스케줄링 | 우선순위 기반 실행 순서 | 충족 | runnable 정렬 후 실행 |
| 스케줄링 | OneShot/Periodic 모드 | 충족 | OneShot 자동 비활성화 포함 |
| 스케줄링 | 오버런 정책(FixedRate/Delay/Skip) | 충족 | 다음 실행 시각 계산 정책 반영 |
| 스케줄링 | 선점형(Preemptive) 실행 | 미충족 | 협력형 async 실행 모델 |
| 시간 모델 | 단조 증가 시계 기반 결정론 | 부분 충족 | DateTimeOffset.Now + Task.Delay 사용 |
| 동기화 | Semaphore | 충족 | timeout/cancellation/dispose 처리 |
| 동기화 | Mutex + owner 검증 | 충족 | owner 추적 및 dispose 처리 |
| 동기화 | EventFlags(wait any/all) | 충족 | timeout/cancellation/dispose 구분 |
| 통신 | MessageQueue FIFO | 충족 | capacity + timeout/cancellation |
| 타이머 | one-shot/periodic timer | 충족 | periodic 예외 복원 + 오류 이벤트 보고 |
| 안정성 | dispose 경로 일관성 | 부분 충족 | 주요 primitive는 개선됨, 전체 정책 문서화 필요 |
| 관측성 | runtime snapshot 통계 | 충족 | duration/deadline miss/state 포함 |
| 관측성 | trace log | 충족 | 등록/실행/오류 등 로그 가능 |
| 커널 모델 | ISR/인터럽트 우선순위 | 미충족 | 사용자 공간 async 모델 |
| 커널 모델 | 태스크 스택/문맥교환 시뮬레이션 | 미충족 | .NET Task 기반으로 대체 |

---

## 3. 항목별 근거 코드(요약)

- 스케줄링 루프/정렬/실행: `Wpf.Lib.RTOS/SchedulerService.cs`
- 실행 정책 enum: `Wpf.Lib.RTOS/CommonRTOS.cs`
- 태스크 규약: `Wpf.Lib.RTOS/IScheduledTask.cs`
- 세마포어: `Wpf.Lib.RTOS/RtosSemaphore.cs`
- 뮤텍스: `Wpf.Lib.RTOS/RtosMutex.cs`
- 이벤트 플래그: `Wpf.Lib.RTOS/RtosEventFlags.cs`
- 메시지 큐: `Wpf.Lib.RTOS/RtosMessageQueue.cs`
- 소프트웨어 타이머: `Wpf.Lib.RTOS/RtosSoftwareTimer.cs`
- tick 계산: `Wpf.Lib.RTOS/RtosTickCounter.cs`

---

## 4. 다음 단계 로드맵

### 단계 A: 시뮬레이터 정확도 향상(권장)

1. 스케줄러 내부 시간 기준을 단조 증가 시계(예: Stopwatch 기반)로 전환
2. 가상 시간 모드(테스트 전용) 도입
3. 스케줄링 지연/지터 측정 지표 추가

완료 기준:

- 같은 입력 시나리오에서 반복 실행 결과의 재현성이 높아짐

### 단계 B: RTOS 개념 재현 강화

1. 우선순위 역전 시나리오 테스트 세트 확장
2. 타이머 드리프트 측정 및 보정 옵션 제공
3. 이벤트 플래그/큐 블로킹 패턴 시나리오 문서화

완료 기준:

- RTOS 교재의 대표 동기화 사례를 대부분 재현 가능

### 단계 C: 고급 시뮬레이터 확장(선택)

1. 가상 인터럽트 이벤트 큐 모델
2. soft-preemption 실험 모드(학습용)
3. 스케줄링 trace replay 도구

완료 기준:

- 학습자가 스케줄러 결정을 타임라인으로 재생/분석 가능

---

## 5. 결론

현재 라이브러리는 "RTOS 스타일 학습 시뮬레이터" 역할을 충분히 수행하고 있습니다.

다만 "엄밀한 RTOS 시뮬레이터"로 올라가려면 다음 2가지가 가장 중요합니다.

1. 시간 모델의 결정론 강화
2. 커널 수준 동작(선점/인터럽트) 재현 범위 확장
