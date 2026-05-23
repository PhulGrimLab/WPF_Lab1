# WPF RTOS Demo

WPF 기반 RTOS 학습/시뮬레이션 데모 프로그램입니다.
태스크 스케줄링, 우선순위, 협력형 선점, 동기화 primitive 동작을 화면에서 확인할 수 있습니다.

## 주요 기능

1. Scheduler 실행/정지
- 태스크 등록 및 주기 실행
- 실시간 상태 스냅샷 표시

2. 우선순위 기반 실행
- 실행 가능 태스크를 우선순위 순으로 처리

3. 협력형 선점 데모
- LOW/HIGH 태스크 흐름 시각화
- 양보(ShouldYield) 동작 확인
- 현재 실행 태스크, 실행 TID, 이벤트 로그 표시

4. RTOS Primitive
- Semaphore
- Mutex
- EventFlags
- MessageQueue
- SoftwareTimer
- TickCounter

5. 내장 테스트
- UI의 RTOS Tests 탭에서 Run Tests 실행
- 기능별 PASS/FAIL 결과 확인

## 화면 구성

1. RTOS Monitor
- 태스크 상태, 실행 횟수, 실행 시간, 다음 실행 시각 등 표시

2. 선점 데모
- 협력형 선점 흐름과 실행 스레드 정보 표시

3. RTOS Tests
- 테스트 러너 결과 확인

## 프로젝트 구조

```text
Wpf.Lib.RTOS      // 스케줄러/primitive 라이브러리
WpfSamples        // WPF 데모 앱(UI + 테스트 실행)
WpfSamples.Tests.Cli // 선점/스케줄러 검증용 콘솔 테스트 러너
Docs              // 초보자/심화 문서
```

## 실행 방법

1. 솔루션 빌드
```bash
dotnet build WpfSamples/WpfSamples.sln
```

2. 샘플 실행
```bash
dotnet run --project WpfSamples/WpfSamples.csproj
```

3. 선점 관련 CLI 테스트 실행
```bash
dotnet run --project WpfSamples.Tests.Cli/WpfSamples.Tests.Cli.csproj
```

4. 반복 실행(재현성 확인)
```bash
dotnet run --project WpfSamples.Tests.Cli/WpfSamples.Tests.Cli.csproj -- --repeat 3
```

5. time quantum 비교 실행(3ms/4ms/5ms)
```bash
dotnet run --project WpfSamples.Tests.Cli/WpfSamples.Tests.Cli.csproj -- --compare-quantum
```

## 참고

1. 본 프로젝트는 학습용 RTOS 시뮬레이터입니다.
2. 실제 RTOS 커널(인터럽트 기반 강제 선점, 하드 실시간 보장)과는 차이가 있습니다.
3. 선점 판정은 RTOS 태스크 간 동작을 기준으로 하며, UI 스레드는 판정 대상에서 제외합니다.

## 문서

1. [Docs/RTOS_Beginner_Quick_Start.md](Docs/RTOS_Beginner_Quick_Start.md)
2. [Docs/RTOS_Beginner_Complete_Guide.md](Docs/RTOS_Beginner_Complete_Guide.md)
3. [Docs/RTOS_Test_Guide.md](Docs/RTOS_Test_Guide.md)
4. [Docs/RTOS_Primitives_Deep_Dive.md](Docs/RTOS_Primitives_Deep_Dive.md)
5. [Docs/RTOS_Real_vs_Simulator_Gap.md](Docs/RTOS_Real_vs_Simulator_Gap.md)
6. [Docs/RTOS_Junior_Code_Maintenance_Guide.md](Docs/RTOS_Junior_Code_Maintenance_Guide.md)
7. [Docs/RTOS_Scheduler_Implementation_Manual_For_Juniors.md](Docs/RTOS_Scheduler_Implementation_Manual_For_Juniors.md)
8. [Docs/RTOS_Junior_HandsOn_Labs.md](Docs/RTOS_Junior_HandsOn_Labs.md)

## 추천 문서 읽기 순서

처음 학습할 때는 아래 순서로 읽는 것을 권장합니다.

1. [Docs/RTOS_Beginner_Quick_Start.md](Docs/RTOS_Beginner_Quick_Start.md)
- 30분 기준으로 핵심 흐름을 빠르게 잡을 수 있습니다.

2. [Docs/RTOS_Scheduler_Implementation_Manual_For_Juniors.md](Docs/RTOS_Scheduler_Implementation_Manual_For_Juniors.md)
- 최초 구현부터 선점/스케줄링/테스트 고도화까지 재구현 관점으로 이해할 수 있습니다.

3. [Docs/RTOS_Junior_HandsOn_Labs.md](Docs/RTOS_Junior_HandsOn_Labs.md)
- Lab 1→2→3 순서로 실제 구현 연습을 진행할 수 있습니다.

4. [Docs/RTOS_Junior_Code_Maintenance_Guide.md](Docs/RTOS_Junior_Code_Maintenance_Guide.md)
- 기존 코드 수정/보완 시 변경 포인트와 검증 절차를 확인할 수 있습니다.

5. [Docs/RTOS_Test_Guide.md](Docs/RTOS_Test_Guide.md)
- 수정 후 테스트 실행/해석 방법을 빠르게 확인할 수 있습니다.

심화 학습은 아래 순서로 권장합니다.

1. [Docs/SchedulerService_Flow.md](Docs/SchedulerService_Flow.md)
2. [Docs/RTOS_Primitives_Deep_Dive.md](Docs/RTOS_Primitives_Deep_Dive.md)
