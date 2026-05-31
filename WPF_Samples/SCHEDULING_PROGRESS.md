# Scheduling Progress

## 2026-05-25 Documentation Sync

- 현재까지 반영한 성능/구조 변경 내용을 README와 핵심 가이드에 반영했다.
- 업데이트한 파일:
   - `README.md`
   - `Docs/SchedulerService_Flow.md`
   - `Docs/Samples_RTOS_Guide.md`
   - `Docs/RTOS_Primitives_Deep_Dive.md`

- 반영 요약:
   - 선점 판정 경로의 캐시화
   - EventFlags `WaitAny` 인덱싱
   - snapshot dirty check
   - 로그/trace 경량화

## 2026-05-24 Preemption Log Throttling / UI Bottleneck Mitigation

사용자 이슈:

1. 선점 데모 탭 로그가 너무 자주 갱신되어 간헐적으로 화면이 끊겨 보임

수정 내용:

1. `WpfSamples/MainWindowViewModel.cs`
   - 선점 로그 UI 반영을 80ms tick마다 즉시 갱신하던 방식에서 **1초 배치 누적 반영**으로 변경
   - 로그 갱신을 전체 Clear/Add 대신, 기존 헤드 기준으로 신규 항목만 `Insert(0, ...)` 하는 증분 반영으로 변경
   - 로그 표시 수 상한(100)을 별도 trim으로 유지
   - `OnPreemptionSchedulerSnapshotChanged`에서 Dispatcher `BeginInvoke`를 매 스냅샷마다 쌓지 않도록 코얼레싱 플래그(`_isPreemptionSnapshotApplyQueued`) 추가

2. `WpfSamples/MainWindow.xaml`
   - 선점 로그 `ListBox`에 가상화 옵션 적용
     - `VirtualizingPanel.IsVirtualizing=True`
     - `VirtualizingPanel.VirtualizationMode=Recycling`
     - `ScrollViewer.CanContentScroll=True`

검증:

- `dotnet build WpfSamples\WpfSamples.sln` 성공
- `dotnet run --project WpfSamples.Tests.Cli\WpfSamples.Tests.Cli.csproj` 3/3 통과

## 2026-05-24 Update

- Moved sample RTOS tasks into `Wpf.Lib.RTOS/Tasks/SampleTasks.cs`.
- Added `Wpf.Lib.RTOS/Tasks/PreemptionDemo.cs`; WPF now starts/stops the library demo and renders snapshots only.
- Moved dining philosophers demo into `Wpf.Lib.RTOS/Tasks/DiningPhilosophersDemo.cs`; WPF renders philosopher/fork/log snapshots and includes its scheduler tasks in the RTOS monitor active-task list.
- Dining philosophers now releases the first fork immediately if the second fork is unavailable, so "held fork" visualization represents actual eating resource ownership instead of one-sided waiting.
- Added `SchedulerService.GetSnapshot()` so demos and UI can read task state from the RTOS library.
- Validation: `dotnet build WpfSamples\WpfSamples.sln` passed with 0 warnings, 0 errors; `dotnet run --project WpfSamples.Tests.Cli\WpfSamples.Tests.Cli.csproj` passed 3/3 tests.

## 2026-05-24 Architecture Alignment Update

- Added `Wpf.Lib.RTOS/Tasks/SchedulerMonitorDemo.cs`.
- `SchedulerMonitorDemo` now owns `SchedulerService` lifecycle and sample task registration (`CounterTask`, `ClockTask`, `UiRefreshTask`) inside the RTOS library.
- `WpfSamples/MainWindowViewModel.cs` no longer directly registers/owns monitor tasks or scheduler lifecycle for RTOS Monitor tab.
- WPF now subscribes to `SchedulerMonitorDemo.SnapshotChanged` and renders snapshot data only, matching the same pattern already used by preemption and dining demos.
- Validation: `dotnet build WpfSamples\WpfSamples.sln` passed with 0 warnings, 0 errors.

## 2026-05-24 Documentation Alignment Update

- Updated architecture docs to match current ownership model:
   - RTOS library owns monitor/preemption/dining demo execution lifecycle.
   - WpfSamples is responsible for demo command wiring and snapshot rendering.
- Updated files:
   - `README.md`
   - `Docs/RTOS_Beginner_Quick_Start.md`
   - `Docs/RTOS_Beginner_Complete_Guide.md`
   - `Docs/RTOS_File_Guide.md`
   - `Docs/RTOS_Junior_Code_Maintenance_Guide.md`
   - `Docs/RTOS_Scheduler_Implementation_Manual_For_Juniors.md`
   - `Docs/RTOS_Overview.md`
   - `Docs/RTOS_Primitives_Deep_Dive.md`
   - `Docs/SchedulerService_Flow.md`
   - `Docs/Samples_RTOS_Guide.md` (rewritten)

## 2026-05-24 Documentation Terminology Unification

- Unified documentation terminology to reduce ambiguity across architecture descriptions.
- Standardized key terms:
   - `실행 주체`
   - `표시 계층`
   - `스냅샷 상태 반영`
   - `데모 조합`
- Added terminology baseline section in `README.md` and aligned related phrasing in beginner/maintenance/sample guides.

## 2026-05-24 Documentation Terminology Unification (Headings)

- Normalized heading-level wording for consistency across docs.
- Applied changes:
   - `Tasks 연동 설명` -> `데모 조합 연동 설명`
   - `Snapshot 반영 흐름` -> `스냅샷 상태 반영 흐름`
   - UI 중심 표현 일부를 `표시 계층` 용어로 통일
- Updated files:
   - `Docs/Samples_RTOS_Guide.md`
   - `Docs/RTOS_Beginner_Complete_Guide.md`
   - `Docs/RTOS_Beginner_Quick_Start.md`
   - `Docs/RTOS_Junior_Code_Maintenance_Guide.md`
   - `Docs/RTOS_Scheduler_Implementation_Manual_For_Juniors.md`

## 2026-05-24 Runtime Hardening Update

- 목적: 라이브러리 데모가 task까지 포함한 최신 구조에서 정지/이벤트 경로의 안정성 강화.

수정 내용:

1. `Wpf.Lib.RTOS/Tasks/SchedulerMonitorDemo.cs`
   - `StopAsync` 반환형을 `Task<bool>`로 변경해 정지 타임아웃 결과를 상위로 전달.
   - dispose 상태 전이를 lock으로 보호해 경쟁 조건 완화.
   - snapshot 이벤트 호출에 핸들러별 예외 격리 적용.

2. `Wpf.Lib.RTOS/Tasks/PreemptionDemo.cs`
   - `Start()`의 동기 stop 대기 제거(이미 실행 중이면 return).
   - `StopAsync` 반환형을 `Task<bool>`로 변경하고 타임아웃 실패 시 상태를 강제로 끄지 않도록 보완.
   - scheduler/snapshot 이벤트 호출에 핸들러별 예외 격리 적용.

3. `Wpf.Lib.RTOS/Tasks/DiningPhilosophersDemo.cs`
   - `StopAsync` 반환형을 `Task<bool>`로 변경하고 타임아웃 실패 시 상태 불일치 방지.
   - scheduler/snapshot 이벤트 호출에 핸들러별 예외 격리 적용.

4. `WpfSamples/MainWindowViewModel.cs`
   - 데모 stop 결과(`bool`)를 확인해 실패 시 UI 상태를 강제 중지로 바꾸지 않도록 반영.

검증:

- `dotnet build WpfSamples\WpfSamples.sln` 성공
- `dotnet run --project WpfSamples.Tests.Cli\WpfSamples.Tests.Cli.csproj` 3/3 통과

## 2026-05-24 Preemption UI / Dining Concurrency Update

사용자 이슈:

1. 선점 화면이 간헐적으로 끊김
2. 철학자의 식사가 1명씩 순차적으로 보이는 경향

수정 내용:

1. `WpfSamples/MainWindowViewModel.cs`
    - 선점 스냅샷 중복 반영 경로 제거:
       - `PreemptionDemo.SnapshotChanged` 직접 적용 구독을 제거하고 UI 타이머 경로로 일원화
    - 선점 UI 타이머 주기를 30ms -> 80ms로 조정
    - `PreemptionLogs`, `PreemptionYieldTrend`, `PreemptionLowWorkerYields`를
       매 tick 무조건 Clear/Add 하지 않고 변경분이 있을 때만 갱신하도록 최적화
    - `ApplyPreemptionSnapshot`에서 매번 `RefreshCommandStates()` 호출 제거

2. `Wpf.Lib.RTOS/Tasks/DiningPhilosophersDemo.cs`
    - waiter 세마포어 용량을 2 -> `PhilosopherCount - 1`로 조정(현재 4)
    - 철학자 태스크 주기를 동일 50ms 고정에서 index 기반 분산 주기(45ms + index*7ms)로 변경
    - 목적: 실행 순서 고정 편향을 줄이고, 비인접 철학자의 동시 식사 기회를 늘림

검증:

- `dotnet build WpfSamples\WpfSamples.sln` 성공
- `dotnet run --project WpfSamples.Tests.Cli\WpfSamples.Tests.Cli.csproj` 3/3 통과

## 2026-05-24 Dining Concurrent Eating Indicator Update

사용자 요청에 따라 철학자 탭에서 동시 식사 상태를 즉시 확인할 수 있는 지표를 추가했다.

수정 내용:

1. `WpfSamples/MainWindowViewModel.cs`
   - `DiningConcurrentEatingStatus` 속성 추가
   - snapshot에서 현재 식사 인원/세션 최대 식사 인원을 계산
   - 표시 포맷: `동시 식사: X명 (최대 Y명)`

2. `WpfSamples/MainWindow.xaml`
   - 철학자 탭 상단 정보 영역에 동시 식사 지표 TextBlock 추가

검증:

- `dotnet build WpfSamples\WpfSamples.sln` 성공
- `dotnet run --project WpfSamples.Tests.Cli\WpfSamples.Tests.Cli.csproj` 3/3 통과

## Goal

현재 WPF 샘플 프로젝트에서 RTOS처럼 태스크를 주기적으로 실행하고, WPF 커스텀 컨트롤 예제들을 여러 개 동작시킬 수 있는 샘플 프로그램의 기본 스케줄링 구조를 만든다.

초기 목표는 실제 RTOS를 완전히 재현하는 것이 아니라, WPF 애플리케이션에서 안전하게 사용할 수 있는 RTOS 스타일의 태스크 스케줄링 골격을 만드는 것이다.

## Recommended Project Structure

```text
Wpf.Lib.Common
 └─ Scheduling
    ├─ SchedulerService.cs
    ├─ ScheduledTask.cs
    ├─ SchedulerContext.cs
    ├─ TaskPriority.cs
    ├─ TaskExecutionMode.cs
    ├─ SchedulerSnapshot.cs
    └─ IScheduledTask.cs

WpfSamples
 ├─ MainWindow.xaml
 ├─ MainWindowViewModel.cs
 └─ Samples
    ├─ ClockTask.cs
    ├─ CounterTask.cs
    └─ UiRefreshTask.cs
```

## Design Direction

`Wpf.Lib.Common` 프로젝트는 스케줄러 코어를 담는 공용 라이브러리로 사용한다.

`WpfSamples` 프로젝트는 스케줄러를 실제로 사용해서 태스크 상태를 화면에 보여주고, 이후 커스텀 컨트롤 샘플들을 연결하는 WPF 샘플 앱으로 사용한다.

중요한 원칙은 WPF UI 스레드와 스케줄러 실행 루프를 분리하는 것이다. 스케줄러는 백그라운드에서 태스크를 실행하고, UI 갱신은 반드시 WPF `Dispatcher`를 통해 처리한다.

## Initial Core Types

```csharp
public enum TaskPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}
```

```csharp
public enum TaskExecutionMode
{
    Periodic,
    OneShot
}
```

```csharp
public interface IScheduledTask
{
    string Name { get; }
    TaskPriority Priority { get; }
    TimeSpan Period { get; }
    TaskExecutionMode Mode { get; }

    DateTimeOffset NextRunAt { get; set; }
    bool IsEnabled { get; }

    Task ExecuteAsync(SchedulerContext context, CancellationToken cancellationToken);
}
```

```csharp
public sealed class SchedulerContext
{
    public SchedulerContext(DateTimeOffset now)
    {
        Now = now;
    }

    public DateTimeOffset Now { get; }
}
```

## Scheduler Service Concept

```csharp
public sealed class SchedulerService
{
    private readonly List<IScheduledTask> _tasks = [];
    private CancellationTokenSource? _cts;

    public event EventHandler<SchedulerSnapshot>? SnapshotChanged;

    public void Register(IScheduledTask task)
    {
        _tasks.Add(task);
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _ = RunAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.Now;

            var runnableTasks = _tasks
                .Where(x => x.IsEnabled && x.NextRunAt <= now)
                .OrderByDescending(x => x.Priority)
                .ThenBy(x => x.NextRunAt)
                .ToList();

            foreach (var task in runnableTasks)
            {
                await task.ExecuteAsync(new SchedulerContext(now), cancellationToken);

                if (task.Mode == TaskExecutionMode.Periodic)
                {
                    task.NextRunAt = now + task.Period;
                }
            }

            SnapshotChanged?.Invoke(this, SchedulerSnapshot.From(_tasks));

            await Task.Delay(10, cancellationToken);
        }
    }
}
```

## Sample Task Concept

```csharp
public sealed class CounterTask : IScheduledTask
{
    public string Name => "Counter";
    public TaskPriority Priority => TaskPriority.Normal;
    public TimeSpan Period => TimeSpan.FromMilliseconds(100);
    public TaskExecutionMode Mode => TaskExecutionMode.Periodic;
    public DateTimeOffset NextRunAt { get; set; } = DateTimeOffset.Now;
    public bool IsEnabled => true;

    public int Count { get; private set; }

    public Task ExecuteAsync(SchedulerContext context, CancellationToken cancellationToken)
    {
        Count++;
        return Task.CompletedTask;
    }
}
```

## WPF Binding Direction

스케줄러의 상태 변화 이벤트를 ViewModel에서 구독하고, UI 갱신은 `Application.Current.Dispatcher.BeginInvoke`로 처리한다.

```csharp
_scheduler.SnapshotChanged += (_, snapshot) =>
{
    Application.Current.Dispatcher.BeginInvoke(() =>
    {
        Tasks.Clear();

        foreach (var task in snapshot.Tasks)
        {
            Tasks.Add(task);
        }
    });
};
```

## First Implementation Scope

초기 버전에서는 아래 범위까지만 구현한다.

1. 태스크 등록
2. 주기 실행
3. 우선순위 정렬
4. 시작/정지
5. UI 표시용 스냅샷
6. WPF Dispatcher 분리

## Later Expansion Ideas

이후 단계에서 아래 기능을 확장한다.

1. 태스크 실행 시간 측정
2. Deadline miss 감지
3. 태스크 enable/disable
4. Pause/Resume
5. Fixed-rate와 fixed-delay 선택
6. UI Thread Task와 Worker Thread Task 구분
7. 로그/트레이스 뷰어
8. 커스텀 컨트롤 샘플별 태스크 연결

## Next Step

다음 작업에서는 `Wpf.Lib.Common/Scheduling` 폴더를 만들고, 위 구조를 실제 C# 파일로 구현한다.

그 다음 `WpfSamples/MainWindowViewModel.cs`에서 샘플 태스크 2~3개를 등록해서 작은 RTOS 모니터 화면처럼 표시한다.

## 2026-05-22 Update

`Wpf.Lib.RTOS` 프로젝트를 기준으로 `WpfSamples/Samples_RTOS`에 실제 동작하는 샘플 태스크를 추가했다.

추가된 샘플 태스크:

1. `CounterTask`: 100ms 주기로 카운터 값을 증가시킨다.
2. `ClockTask`: 1초 주기로 현재 시간을 갱신한다.
3. `UiRefreshTask`: 250ms 주기로 UI 갱신성 작업을 흉내낸다.

`WpfSamples` 프로그램의 `MainWindowViewModel`에서 `SchedulerService`를 생성하고 위 태스크들을 등록하도록 연결했다.

GUI에서는 Start/Stop 버튼으로 스케줄러를 제어하고, DataGrid를 통해 태스크 이름, 우선순위, 주기, 실행 횟수, 최근 실행 시간, 실행 소요 시간, 다음 실행 예정 시간, 오류 상태를 확인할 수 있다.

검증:

```text
dotnet build WpfSamples\WpfSamples.sln
```

빌드 결과: 경고 0개, 오류 0개.

## 2026-05-22 Beginner Readability Update

`Wpf.Lib.RTOS` 코드를 C# 초보자가 더 쉽게 읽을 수 있도록 코드/문서를 함께 보완했다.

1. 코드 가독성 정리

   `IScheduledTask.cs`, `SchedulerContext.cs`, `ScheduledTask.cs`, `CommonRTOS.cs`에서 불필요한 using을 제거했다.
   `CommonRTOS.cs`의 의미 없는 빈 클래스 `CommonRTOS`를 제거하고 enum 정의 전용 파일로 정리했다.

2. 복잡 구간 해설 주석 보강

   `SchedulerService.cs`의 아래 메서드에 단계형 설명 주석을 추가했다.

   - `CopyRunnableTasks`
   - `ShouldYieldToHigherPriorityTask`
   - `ScheduleNextRunIfRegistered`
   - `GetNextRunAfterMissedTicks`

   `RtosEventFlags.cs`의 대기 수명주기(즉시 완료/등록/timeout/cancel/dispose) 흐름을 주석으로 보강했다.

3. 초보자 문서 보강

   `Docs/RTOS_Beginner_Complete_Guide.md`에
   "초보자가 특히 어려워하는 4개 구간 빠른 해설" 섹션을 추가했다.

   - runnable 후보 선별
   - 협력형 양보 판단
   - EventFlags waiter 생명주기
   - OneShot/Periodic 다음 실행 시각 계산

검증:

```text
dotnet build WpfSamples\WpfSamples.csproj
```

## 2026-05-22 Preemption GUI Clarity Update

사용자 피드백("어떤 태스크가 동작 중인지, 어느 스레드인지 한눈에 보이게")에 맞춰
선점 데모 탭의 가시성을 강화했다.

1. 선점 데모 실시간 인스펙터 추가

   `MainWindow.xaml`의 선점 데모에 "태스크/스레드 실시간 인스펙터" 패널을 추가했다.

   - LOW Priority Worker 마지막 실행 스레드 ID
   - HIGH Priority Urgent 마지막 실행 스레드 ID
   - 현재 동작 태스크(LOW/HIGH/대기)
   - UI 스레드 ID

2. ViewModel 계측 필드 추가

   `MainWindowViewModel`에 아래 상태를 추가했다.

   - `PreemptionLowLastThreadId`
   - `PreemptionHighLastThreadId`
   - `PreemptionActiveTask`
   - `PreemptionUiThreadId`

   선점 데모 태스크 실행 시 `Environment.CurrentManagedThreadId`를 기록하고,
   UI 타이머 tick마다 화면으로 반영한다.

3. 로그 메시지 보강

   선점 로그에 태스크 실행 시점의 TID를 포함해
   화면 카드와 로그를 함께 비교할 수 있게 했다.

검증:

```text
dotnet build WpfSamples\WpfSamples.csproj
```

## 2026-05-22 Preemption Inspector Visual Emphasis

선점 데모 인스펙터 카드가 상태 변화에 따라 색상으로 즉시 구분되도록 개선했다.

1. 동적 카드 강조

   - LOW 동작 중: LOW 카드 녹색 계열 강조
   - HIGH 동작 중: HIGH 카드 빨강 계열 강조
   - 실행 중 구간: "현재 동작 태스크" 카드 파랑 계열 강조
   - 대기/중지: 모든 카드 기본색 복귀

2. ViewModel Brush 속성 추가

   - `PreemptionLowInspectorBackground`, `PreemptionLowInspectorBorder`
   - `PreemptionHighInspectorBackground`, `PreemptionHighInspectorBorder`
   - `PreemptionCurrentInspectorBackground`, `PreemptionCurrentInspectorBorder`

   `PreemptionActiveTask` 값(LOW/HIGH/대기/중지)에 따라 `UpdatePreemptionInspectorHighlight()`에서
   카드 색상을 일관되게 갱신한다.

3. 중지 상태 정합성 개선

   데모 중지 시 `PreemptionActiveTask`를 `중지`로 설정해
   인스펙터와 흐름 상태가 서로 다른 값을 보여주지 않도록 맞췄다.

검증:

```text
dotnet build WpfSamples\WpfSamples.csproj
```

## 2026-05-22 UI Thread vs Execution Thread Clarification

선점 데모의 "현재 동작 태스크" 카드가 UI TID를 중심으로 보여 오해를 줄 수 있어,
실제 실행 스레드와 UI 스레드를 분리 표기하도록 수정했다.

1. 현재 실행 TID 표시 추가

   - `PreemptionCurrentExecutionThreadId`
   - `PreemptionCurrentExecutionThreadLabel`

   활성 태스크가 HIGH면 HIGH의 마지막 실행 TID,
   활성 태스크가 LOW면 LOW의 마지막 실행 TID를 보여준다.
   대기/중지 상태는 `없음`으로 표시한다.

2. 카드 표기 구조 변경

   기존: UI 스레드 TID 강조

   변경:
   - `현재 실행 TID: ...` (강조)
   - `UI 스레드 TID(고정): ...` (보조)

   이를 통해 WPF UI 스레드와 RTOS 데모 실행 스레드가 별개임을
   화면에서 즉시 확인할 수 있다.

검증:

```text
dotnet build WpfSamples\WpfSamples.csproj
```

## 2026-05-22 Preemption Scope Clarification (UI Excluded)

사용자 피드백에 따라 선점 모델의 범위를 더 명확히 했다.

1. 선점 판단 범위 고정

   선점 데모의 판단 대상은 RTOS 태스크(LOW/HIGH)로 한정하며,
   WPF UI 스레드는 선점 판정 대상에서 제외한다.

2. 화면 문구 보강

   선점 흐름 패널에 아래 안내를 추가했다.

   - "선점 판단 범위: RTOS 태스크(LOW/HIGH) 전용. UI 스레드는 선점 판정 대상에서 제외됩니다."

3. 현재 동작 카드 보조 정보 정리

   - 주 정보: 현재 실행 TID(실제 RTOS 데모 실행 스레드)
   - 보조 정보: 참고 UI TID(선점 판단 제외)

검증:

```text
dotnet build WpfSamples\WpfSamples.csproj
```

## 2026-05-22 Beginner Guide Deep Explanation Expansion

요청사항("현재 코드 설명을 C#/RTOS 초보자도 이해할 수 있도록 자세히")에 맞춰
`Docs/RTOS_Beginner_Complete_Guide.md`를 대폭 보강했다.

추가한 핵심 섹션:

1. `20. 현재 코드 기준 상세 해설`
   - Start -> RunAsync -> ExecuteTaskAsync -> Snapshot 흐름을 단계별로 설명
   - CopyRunnableTasks의 2단계 lock 전략 이유 설명
   - 협력형 선점 판단(ShouldYield)의 실제 코드 의미 설명

2. `21. 선점 데모 코드 상세 설명 (UI 스레드 제외 원칙)`
   - RTOS 선점 판단 대상에서 UI 스레드를 제외해야 하는 이유 명시
   - 선점 데모에서 현재 실행 TID/참고 UI TID 분리 표시 의도 설명

3. `22. C# 초보자용 코드 읽기 습관`
   - 시그니처 우선 읽기, 상태 필드/lock 경계 파악, 실패 경로 확인 등
   - 동시성 코드 체크리스트 제공

4. `23. 초보자용 미니 실습 3개`
   - Heartbeat 태스크 추가
   - OverrunPolicy 비교 실험
   - EventFlags 기반 HIGH 태스크 깨우기

비고:

- 문서성 변경이라 빌드는 수행하지 않았다.

## 2026-05-23 MessageQueue Dispose Hang Fix + Full Suite Re-Validation

`RtosMessageQueue`의 dispose 경로에서 대기 중인 `WaitAsync`와 `SemaphoreSlim.Dispose()`가 경합할 때
pending send/receive 테스트가 타임아웃으로 멈출 수 있는 문제를 수정했다.

개선 내용:

1. `RtosMessageQueue.Dispose()` 동작 수정
   - `_isDisposed = true`, `_queue.Clear()` 후 `_shutdownCts.Cancel()`로 대기 작업을 깨우는 방식으로 정리
   - 대기 작업이 남아 있는 동안 `_items.Dispose()`, `_spaces.Dispose()`를 즉시 호출하지 않도록 변경

2. CLI 러너 보강
   - `WpfSamples.Tests.Cli`에 `--tests` 옵션을 추가해 특정 테스트 메서드만 선택 실행 가능하게 개선
   - 신규 MessageQueue dispose 회귀 테스트를 빠르게 단독 검증할 수 있게 함

검증:

```text
dotnet build WpfSamples\WpfSamples.sln
dotnet run --project WpfSamples.Tests.Cli\WpfSamples.Tests.Cli.csproj -- --tests MessageQueuePendingSendCanceledByDisposeAsync,MessageQueuePendingReceiveCanceledByDisposeAsync
dotnet run --project WpfSamples.Tests.Cli\WpfSamples.Tests.Cli.csproj -- --all
```

결과:

- 선택 테스트: `2/2 passed`
- 전체 스위트: `47/47 passed`

## 2026-05-23 Junior Maintenance Guide Added

사용자 요청("주니어 개발자가 코드 구조/흐름을 이해하고 직접 수정 가능한 수준의 설명서")에 맞춰
실무형 유지보수 가이드를 신규 작성했다.

추가 문서:

1. `Docs/RTOS_Junior_Code_Maintenance_Guide.md`

핵심 구성:

1. 코드베이스 파일 맵(어디를 수정하면 어떤 동작이 바뀌는지)
2. 권장 읽기 순서(IScheduledTask -> SchedulerTaskBase -> Context -> SchedulerService 핵심 4메서드)
3. SchedulerService 실행 흐름 상세(Start/Stop, Run loop, runnable 선택, execute, next-run 계산)
4. Primitive 수정 규칙(dispose 경합, timeout/cancel/dispose 의미 분리, lock 범위 최소화)
5. 태스크 추가/수정 표준 절차 + 주니어용 체크리스트
6. 자주 하는 실수와 교정 방법
7. CLI 검증 명령(--all, --tests, --compare-quantum)

연계 반영:

1. `README.md` 문서 목록에 신규 가이드 링크 추가

비고:

- 문서성 변경이라 별도 빌드는 생략했다.

## 2026-05-23 Intermittent UI Stutter Mitigation (Post-Preemption Change)

사용자 보고("선점 코드 변경 이후 UI가 간헐적으로 끊김")에 따라
preemption 경로를 점검하고, UI 응답성 저하를 유발하는 병목을 완화했다.

수정 내용:

1. `WpfSamples/MainWindowViewModel.cs`
   - preemption 로그 큐에 백프레셔 추가
   - pending 로그 상한(`MaxPendingPreemptionLogs`) 도입
   - UI tick당 로그 배출량 제한(`MaxPreemptionLogsDrainPerTick`) 적용
   - 과다 로그는 누적 후 "생략 건수"로 요약 표시
   - 효과: 한 tick에서 과도한 `ObservableCollection.Insert(0, ...)` 연산으로 UI가 멈추는 현상 완화

2. `Wpf.Lib.RTOS/SchedulerService.cs`
   - `ShouldYieldToHigherPriorityTask`에서 매 호출마다 candidate 리스트를 새로 복사하던 할당 제거
   - `_tasks` 직접 순회로 GC pressure 감소

검증:

```text
dotnet build WpfSamples\WpfSamples.sln
dotnet run --project WpfSamples.Tests.Cli\WpfSamples.Tests.Cli.csproj -- --tests SchedulerCooperativePreemptionYieldsToHigherPriorityAsync,SchedulerPreemptedTaskResumesImmediatelyAsync,SchedulerTimeQuantumPreemptsLongTaskAsync
```

결과:

- 솔루션 빌드 성공

## 2026-05-23 Preemption Bottom Log 1s Aggregation

사용자 요청("선점 데모 하단 로그가 너무 자주 출력되어 UI 끊김 가능")에 맞춰
로그 출력 방식을 건별 갱신에서 1초 누적 요약 갱신으로 변경했다.

수정 내용:

1. `WpfSamples/MainWindowViewModel.cs`
   - `DrainPreemptionLogs(force = false)`로 확장
   - 기본 동작: 1초마다 pending 로그를 모아 요약 1건만 `PreemptionLogs`에 추가
   - 정지 동작: `force: true`로 즉시 flush
   - 요약 예: "1초 요약: 신규 이벤트 N건, 생략 M건 / 최근: ..."

2. UI 병목 점검 결과
   - 고빈도 ListBox Insert(하단 로그)가 주요 체감 병목으로 판단
   - ActiveTasks 재구성은 이미 변경 감지 후 재구성하도록 최적화되어 있음

3. 문서 반영
   - `Docs/RTOS_Junior_Code_Maintenance_Guide.md`
   - `Docs/RTOS_Scheduler_Implementation_Manual_For_Juniors.md`
   - 로그 1초 배치 요약 전략과 병목 점검 포인트 추가

## 2026-05-23 State Semantics Documentation Expansion

사용자 요청("Blocked/Ready 상태 설명을 매뉴얼에 명확히 추가")에 맞춰
상태 정의/전이표/해석 가이드를 문서에 보강했다.

반영 문서:

1. `Docs/RTOS_Beginner_Complete_Guide.md`
   - `Ready/Blocked/Running/Suspended` 정의 추가
   - 상태 전이표(조건 -> 다음 상태) 추가
   - RTOS Monitor와 선점 데모에서 상태가 다르게 보이는 이유 추가

2. `Docs/RTOS_Scheduler_Implementation_Manual_For_Juniors.md`
   - 재구현 관점 상태 의미 고정 섹션 추가
   - 상태 전이표 및 실무 체크 포인트 추가

비고:

- 문서성 변경이라 별도 빌드는 생략했다.

## 2026-05-23 Starvation Sensitivity UI + Documentation Expansion

사용자 요청("기아 판정 민감도 조절 기능 추가 + 설명서 상세 반영")에 맞춰
선점 데모 UI와 문서를 함께 보강했다.

수정 내용:

1. `WpfSamples/MainWindowViewModel.cs`
   - `PreemptionStarvationLowStallSeconds` (1~8초) 속성 추가
   - `PreemptionStarvationLowStallLabel` 표시 문자열 추가
   - 기아 판정 로직의 LOW 정체 임계값(기존 고정 3초)을 설정값으로 대체

2. `WpfSamples/MainWindow.xaml`
   - 선점 데모 상단에 "기아 감지 민감도" 슬라이더 추가
   - 현재 임계값(초)을 라벨로 실시간 표시

3. 문서 보강
   - `Docs/RTOS_Junior_Code_Maintenance_Guide.md`
     - 로그 누적 출력, ActiveTasks 재구성 최적화, 기아 민감도 튜닝 가이드 추가
   - `Docs/RTOS_Scheduler_Implementation_Manual_For_Juniors.md`
     - 기아 경고 설계 근거(RunCount/LastStartedAt)와 UI 끊김 방지 원칙 추가

검증:

```text
dotnet build WpfSamples\WpfSamples.sln
```

결과:

- 솔루션 빌드 성공

## 2026-05-23 Preemption Starvation Warning Added

사용자 요청("LOW 기아 여부를 RunCount/LastStartedAt 기준으로 자동 판정")에 맞춰
선점 데모에 기아 경고 표시를 추가했다.

수정 내용:

1. `WpfSamples/MainWindowViewModel.cs`
    - preemption snapshot 기반 기아 판정 로직 추가 (`UpdatePreemptionStarvationSignal`)
    - 판정 근거:
       - LOW RunCount 증가 정체
       - LOW LastStartedAt 갱신 정체
       - HIGH/Normal 진행이 최근에도 지속
       - 상위 우선순위 task가 Ready/Running 상태 유지
    - UI 바인딩용 상태 추가:
       - `PreemptionStarvationStatus`
       - `PreemptionStarvationForeground`
    - health 상태 갱신에서 starvation 감지 시 `주의`로 우선 표기

2. `WpfSamples/MainWindow.xaml`
    - 선점 데모 패널에 "기아 감지" 상태 문구 라인 추가

검증:

```text
dotnet build WpfSamples\WpfSamples.sln
```

결과:

- 솔루션 빌드 성공
- 선점 핵심 회귀 테스트 3/3 통과

## 2026-05-23 Additional Log Frequency Reduction + UI Refresh Guard

사용자 요청("로그 빈도를 더 줄이고, UI 갱신 끊김 가능 구간 확인")에 맞춰
preemption 로그/갱신 경로를 추가 최적화했다.

수정 내용:

1. `WpfSamples/MainWindowViewModel.cs` 로그 정책 변경
    - 건별 로그 중심에서 "누적 N회마다 1회 요약" 방식으로 전환
    - `QueuePreemptionLogEvery(key, every, message)` 추가
    - 적용 예:
       - Normal Telemetry: 5회마다 1회
       - High Urgent 시작: 3회마다 1회
       - Low Worker 양보: worker별 4회마다 1회
       - Low Worker 완료: worker별 2회마다 1회
    - burst 단계 로그는 세부 건별 출력 대신 누적 요약 출력으로 축소

2. UI 갱신 경로 최적화
    - `SyncActiveTasks`에서 활성 태스크 집합이 이전과 동일하면
       `RebuildActiveTasks()`를 호출하지 않도록 변경
    - 효과: snapshot 주기마다 `ActiveTasks.Clear()/Add(...)`가 반복되던 불필요 갱신 감소

검증:

```text
dotnet build WpfSamples\WpfSamples.sln
```

결과:

- 솔루션 빌드 성공

## 2026-05-23 Junior Hands-on Labs Package Added

사용자 요청("주니어가 RTOS 라이브러리를 보고 다른 프로젝트에서 스케줄러를 구현할 수 있을 정도의 단계별 매뉴얼")에 맞춰,
실습형 3단계 패키지 문서를 신규 작성했다.

추가 문서:

1. `Docs/RTOS_Junior_HandsOn_Labs.md`

핵심 구성:

1. Lab 1: 최소 스케줄러 구현(Periodic/OneShot/Priority/Start-Stop/Snapshot)
2. Lab 2: 선점 구현(ShouldYield, timeQuantum 근사 선점, overrun 정책)
3. Lab 3: 현재 프로젝트 테스트 케이스 적용(스케줄러 + primitive 회귀)
4. 단계별 DoD(완료 기준)와 디버깅 고정 루틴 포함

연계 반영:

1. `README.md` 문서 목록에 신규 실습 패키지 링크 추가

비고:

- 문서성 변경이라 별도 빌드는 생략했다.

## 2026-05-23 Re-Implementation Manual for Junior Developers

사용자 요청("주니어가 다른 프로젝트에서 별도 RTOS 스케줄러를 직접 구현할 수 있는 수준")에 맞춰,
구현 절차 중심의 상세 매뉴얼을 신규 작성했다.

추가 문서:

1. `Docs/RTOS_Scheduler_Implementation_Manual_For_Juniors.md`

문서 구성 핵심:

1. C# 환경에서 RTOS를 어떻게 해석해야 하는지(협력형 선점 + quantum 근사) 전제 정의
2. 필수 아키텍처 컴포넌트(IScheduledTask, Context, Service, Runtime/Snapshot) 정리
3. 0단계~8단계 구현 로드맵(최초 루프 -> 고도화 -> 선점 -> primitive -> 운영성)
4. 선점/스케줄링 핵심 메서드 설계 이유와 동작 근거 설명
5. 처음 구현부터 현재 테스트 케이스 적용까지의 단계별 테스트 확장 전략
6. 재구현 체크리스트/안티패턴 포함

연계 반영:

1. `README.md` 문서 목록에 신규 매뉴얼 링크 추가

비고:

- 문서성 변경이라 별도 빌드는 생략했다.

## 2026-05-22 Quick Start Practice Expansion

사용자 요청("초보자가 코드와 설명을 보고 바로 이해")에 맞춰
`Docs/RTOS_Beginner_Quick_Start.md`를 실습 중심으로 재구성했다.

개선 내용:

1. 단계형 학습 구조 강화
   - 0~30분 구간별 목표/확인 포인트를 명확히 분리
   - 각 단계마다 "무엇을 보면 되는지"를 파일/메서드 중심으로 제시

2. 코드 예시 추가
   - ScheduledTask 생성 최소 예시(Heartbeat)를 삽입해
     초보자가 바로 따라해볼 수 있게 구성

3. 선점 시뮬레이터 최신 정책 반영
   - 협력형 선점(ShouldYield) 설명 보강
   - UI 스레드 제외 원칙(RTOS 태스크 간 선점 판정) 명시

4. 실습/검증 중심 섹션 추가
   - 미니 실습 A/B/C(태스크 추가, 오버런 정책 비교, 양보 확인)
   - 실습 후 확인할 관찰 지표(RunCount, TID, 로그 등) 제공

5. FAQ 확장
   - 기존 3개에서 5개로 확대
   - StopAsync 지연, Start/Done TID 차이 등 실제 질문 빈도 높은 항목 반영

비고:

- 문서성 변경이라 빌드는 수행하지 않았다.

## 2026-05-22 Test Guide Practice Expansion

`Docs/RTOS_Test_Guide.md`를 초보자 실습형으로 재작성하고,
현재 `RtosTestRunner.RunAllAsync()`의 실제 테스트 목록과 맞춰 정리했다.

개선 내용:

1. 실행 안내 단순화
   - UI에서 테스트 실행하는 단계와 결과 컬럼 해석을 명확히 정리

2. 최신 테스트 목록 동기화
   - Semaphore/MessageQueue/EventFlags/Mutex/SoftwareTimer/TickCounter/Scheduler
   - Scheduler 협력형 선점/오버런/스냅샷/오류 내성 항목까지 반영

3. 초보자 디버깅 루틴 추가
   - FAIL 발생 시 Message -> 테스트 메서드 -> 대상 클래스 순으로 추적
   - timeout/cancellation/dispose 분리 점검 체크리스트 포함

4. 테스트 추가 가이드 보강
   - 메서드 작성/배열 등록/네이밍 패턴 제시

5. 선점 테스트 해석 주의사항 추가
   - 협력형 선점 모델(ShouldYield) 설명
   - UI 스레드 선점 판정 제외 원칙 명시

비고:

- 문서성 변경이라 빌드는 수행하지 않았다.

## 2026-05-22 Docs Full Refresh (Remaining Documents)

사용자 요청("남은 문서 전체 개선")에 따라, Docs 폴더의 남은 문서를
초보자 관점 + 최신 코드 정책 기준으로 일괄 보강했다.

대상 문서:

1. `Docs/RTOS_Overview.md`
2. `Docs/RTOS_File_Guide.md`
3. `Docs/Samples_RTOS_Guide.md`
4. `Docs/SchedulerService_Flow.md`
5. `Docs/RTOS_Primitives_Deep_Dive.md`
6. `Docs/RTOS_Simulator_Maturity_Checklist.md`
7. `Docs/RTOS_Real_vs_Simulator_Gap.md`

핵심 개선 축:

1. 용어 통일
   - 협력형 선점(ShouldYield) 정의 통일
   - UI 스레드 선점 판정 제외 원칙 명시

2. 초보자 학습 동선 강화
   - 파일/함수 우선 읽기 순서 제시
   - 개념 -> 코드 -> 테스트 -> UI 관찰 흐름으로 연결

3. 최신 기능 반영
   - 선점 데모 인스펙터(TID/활성 태스크) 해석 추가
   - StopAsync(timeout), 자기중지 보호 등 최신 동작 설명 보강

4. 실전 적용 가이드 강화
   - 시뮬레이터 성숙도 판정 기준 보강
   - 실제 RTOS와의 경계(가능/제한)를 FAQ 형태로 명확화

비고:

- 문서성 변경이라 빌드는 수행하지 않았다.

## 2026-05-22 RTOS Runtime Safety Hardening

RTOS 라이브러리 전체 점검에서 발견된 누수/경합 가능 지점을 수정하고
회귀 테스트를 추가했다.

1. `RtosEventFlags` waiter 잔류 방지

문제:

- `WaitAsync`에서 waiter를 `_waiters`에 먼저 등록한 뒤
   `StartTimeout` 등록 과정에서 예외가 나면 orphan waiter가 남을 수 있었다.

수정:

- `WaitAsync`에서 `request.StartTimeout(...)`를 `try/catch`로 감싸고
   실패 시 `RemoveWaiter(request)`로 목록에서 즉시 제거.
- `WaitRequest.StartTimeout` 내부도 `try/catch`로 감싸
   부분 등록 상태에서 `Cleanup()` 후 예외 재던지기 처리.

2. `RtosSoftwareTimer` stop/start 경합 완화

문제:

- `StopAsync`가 `_runTask`를 먼저 null로 만들던 구간에서
   `Start()`가 들어오면 기존 run과 신규 run이 겹칠 가능성이 있었다.

수정:

- `StopAsync`에서 즉시 `_runTask`를 null로 비우지 않고,
   기존 run 종료(await) 후 참조 일치 시 null 처리.
- `Dispose` 경로도 동일 원칙으로 변경해
   run task 종료 시점에 안전하게 `_runTask`를 정리.

3. 테스트 추가

`WpfSamples/Tests/RtosTestRunner.cs`에 아래 테스트 추가:

- `EventFlags registration failure does not leak waiter`
- `SoftwareTimer stop-start race avoids overlapping runs`

4. 검증

```text
dotnet build WpfSamples\WpfSamples.csproj
```

결과: 성공 빌드.

## 2026-05-22 Docs Beginner Improvement Update

Docs 폴더 문서를 다시 점검해, C# 초보자가 코드와 문서를 연결해 읽기 쉽도록 구조를 보강했다.

1. 빠른 시작 문서 추가

   새 문서 `Docs/RTOS_Beginner_Quick_Start.md`를 추가했다.
   30분 기준으로 "어떤 파일을 어떤 순서로 보면 되는지"를 단계별로 정리했다.

2. 문서 허브 연결 강화

   `Docs/RTOS_Overview.md`에 빠른 시작 링크와 초보자 추천 읽기 순서를 추가했다.
   `Docs/RTOS_Beginner_Complete_Guide.md` 초반에 빠른 시작 문서 안내를 추가했다.

3. 최신 코드 반영 보강

   `Docs/RTOS_File_Guide.md`에 최신 코드 기준 보완 섹션을 추가했다.

   - IScheduledTask의 OverrunPolicy, SetEnabled
   - SchedulerContext.ShouldYield
   - ScheduledTaskSnapshot 확장 통계 필드
   - StopAsync(timeout) 동작

4. 테스트 문서 가독성 보강

   `Docs/RTOS_Test_Guide.md`에 기능별 테스트 매핑 섹션을 추가했다.
   기능(스케줄러/오버런/primitive) 기준으로 어떤 테스트를 먼저 읽을지 안내했다.

## 2026-05-22 App Exit Fix Update

윈도우 창의 X 버튼으로 닫을 때 창만 사라지고 프로세스가 남는 현상을 보완했다.

원인 후보:

1. `MainWindowViewModel.Dispose()`에서 `StopPreemptionTestAsync().GetAwaiter().GetResult()`로 비동기 정지를 UI 스레드에서 동기 대기해 교착 가능성 존재
2. 종료 경로에서 앱 종료 모드/강제 종료 보장이 약함

수정 내용:

1. `MainWindowViewModel`에 `IAsyncDisposable` 구현 추가
2. `Dispose()`는 UI 스레드에서 동기 대기하지 않고 `DisposeAsync()`를 비동기 호출
3. `MainWindow.OnClosed`를 `async void`로 변경하고 `await _viewModel.DisposeAsync()` 처리
4. `App.xaml`에 `ShutdownMode="OnMainWindowClose"` 명시
5. `OnClosed` 종료 후 `Application.Current.Shutdown()` 호출로 종료 보강

검증:

```text
dotnet build WpfSamples\WpfSamples.csproj
```

## 2026-05-22 RTOS Simulator Features and Tests Update

실제 임베디드 RTOS 개념에 조금 더 가까워지도록 `Wpf.Lib.RTOS`에 시뮬레이터 기능을 추가하고, `WpfSamples`에서 UI로 실행할 수 있는 단위 테스트를 추가했다.

추가 기능:

1. 태스크 상태

   `Enum_TaskState`를 추가했다.
   상태 값은 `Ready`, `Running`, `Blocked`, `Suspended`이다.
   `ScheduledTaskSnapshot`에 `State` 필드를 추가했고, WPF DataGrid에도 `State` 컬럼을 추가했다.

2. `RtosSemaphore`

   RTOS semaphore 개념을 흉내낸 클래스이다.
   `WaitAsync`와 `Release`를 통해 제한된 자원 접근을 테스트할 수 있다.

3. `RtosMessageQueue<T>`

   태스크 사이 메시지 전달을 흉내낸 bounded queue이다.
   `SendAsync`와 `ReceiveAsync`를 제공한다.

4. `RtosTickCounter`

   RTOS tick 개념을 단순화한 클래스이다.
   기준 시각과 tick interval을 기준으로 현재 tick을 계산한다.

테스트 코드:

```text
WpfSamples/Tests/RtosAssert.cs
WpfSamples/Tests/RtosTestResult.cs
WpfSamples/Tests/RtosTestRunner.cs
```

현재 테스트 항목:

1. Semaphore wait/release
2. MessageQueue send/receive
3. TickCounter converts time to ticks
4. Scheduler executes periodic task
5. Scheduler snapshot contains task state

WPF UI 변경:

1. 기존 RTOS 상태 화면을 `RTOS Monitor` 탭으로 구성했다.
2. 테스트 실행 화면을 `RTOS Tests` 탭으로 추가했다.
3. `Run Tests` 버튼으로 테스트를 실행하고 결과를 DataGrid에서 확인할 수 있게 했다.

문서 추가:

```text
Docs/RTOS_Test_Guide.md
```

검증:

```text
dotnet build WpfSamples\WpfSamples.sln
```

빌드 결과: 경고 0개, 오류 0개.

## 2026-05-22 Documentation Update

`Wpf.Lib.RTOS` 라이브러리와 `WpfSamples/Samples_RTOS` 샘플 태스크를 C# 초보자도 이해할 수 있도록 `Docs` 폴더에 설명서를 추가했다.

추가 문서:

1. `Docs/RTOS_Overview.md`

   RTOS 스타일 스케줄러의 전체 개념, 핵심 용어, 전체 실행 흐름, WPF에서 UI 스레드와 스케줄러를 분리해야 하는 이유를 설명한다.

2. `Docs/RTOS_File_Guide.md`

   `Wpf.Lib.RTOS` 프로젝트의 파일별 역할을 설명한다.
   `IScheduledTask`, `SchedulerTaskBase`, `ScheduledTask`, `SchedulerService`, snapshot 관련 record, runtime info 구조를 파일별로 정리했다.

3. `Docs/SchedulerService_Flow.md`

   `SchedulerService` 내부 동작 흐름을 단계별로 설명한다.
   태스크 등록, 시작, 실행 후보 선택, 태스크 실행, 다음 실행 시각 계산, snapshot 발행, 정지, dispose 흐름을 다룬다.

4. `Docs/Samples_RTOS_Guide.md`

   `CounterTask`, `ClockTask`, `UiRefreshTask`가 `IScheduledTask`를 통해 RTOS 라이브러리와 어떻게 연결되는지 설명한다.
   `MainWindowViewModel`, `SnapshotChanged`, `Dispatcher`, `ScheduledTaskStatusViewModel`, `DataGrid`까지 이어지는 GUI 갱신 흐름도 함께 정리했다.

## 2026-05-22 RTOS Library Validation Update

`Wpf.Lib.RTOS` 코드 리뷰에서 확인한 추가 개선 항목 중 1번부터 3번까지 반영했다.

1. `StopAsync(TimeSpan timeout)` 사전 검증

   기존에는 timeout 값 검증이 `WaitForStopAsync` 내부에서 수행되어, 잘못된 timeout이 들어오면 스케줄러 상태가 `Stopping`으로 바뀐 뒤 예외가 발생할 수 있었다.
   `StopAsync` 시작 시점에 `ValidateTimeout`을 호출하도록 변경해 상태 변경 전에 잘못된 인자를 차단했다.

2. `SchedulerService` interval 검증

   `tickInterval`과 `snapshotInterval`이 0 이하인 경우 예외를 발생시키도록 생성자 검증을 추가했다.
   잘못된 interval 값으로 busy loop나 과도한 snapshot 발생이 생기는 것을 방지한다.

3. `task.Status` 예외 보호

   스냅샷 생성 중 외부 태스크의 `Status` 접근에서 예외가 발생해도 스케줄러 루프가 종료되지 않도록 `GetTaskStatus`를 추가했다.
   예외가 발생하면 `"Status error: ..."` 문자열을 snapshot에 담는다.

검증:

```text
dotnet build WpfSamples\WpfSamples.sln
```

빌드 결과: 경고 0개, 오류 0개.

## 2026-05-22 RTOS Library Cleanup Update

`Wpf.Lib.RTOS`의 스케줄러 종료/정리 경로와 실행 후보 처리 방식을 추가로 개선했다.

1. `Dispose()` 리소스 정리 보강

   기존 `Dispose()`는 `CancellationTokenSource.Cancel()`만 호출하고 dispose하지 않았다.
   `cts?.Dispose()`를 추가해 스케줄러 인스턴스를 반복 생성/폐기할 때 리소스가 남지 않도록 했다.

2. `RunAsync` 종료 경로 정리

   `RunAsync`가 정상 취소, 내부 예외, 기타 종료 경로로 끝나더라도 `finally`에서 `CleanupStoppedScheduler`를 호출하도록 변경했다.
   스케줄러 루프가 예외로 종료된 뒤에도 상태가 `Running` 또는 `Stopping`에 남는 문제를 줄였다.

3. `StopAsync(timeout)` timeout 이후 정리 보장

   `StopAsync(TimeSpan timeout)`이 제한 시간 안에 종료하지 못해 `false`를 반환하더라도, 실행 루프가 나중에 끝나면 cleanup이 호출되도록 continuation을 추가했다.
   timeout 대기용 `Task.Delay`는 `CancellationTokenSource`로 취소할 수 있게 변경해 불필요한 타이머 유지 시간을 줄였다.

4. 실행 후보 선별 lock 범위 축소

   `_syncRoot` lock 안에서는 등록된 태스크 목록 복사만 수행하고, `task.IsEnabled`, `task.NextRunAt` 같은 외부 태스크 속성 평가는 lock 밖에서 수행하도록 변경했다.
   사용자 태스크 구현의 속성 접근이 느리거나 내부 lock을 사용하는 경우에도 스케줄러의 등록/해제/스냅샷 작업이 오래 막히지 않도록 했다.

5. 제거된 태스크 실행 방지

   실행 후보가 `_executionBuffer`에 복사된 뒤 `Unregister` 또는 `Clear`가 호출될 수 있으므로, 실제 실행 직전에 등록 여부를 다시 확인하도록 했다.
   제거된 태스크가 현재 tick에서 한 번 더 실행되는 가능성을 줄였다.

검증:

```text
dotnet build WpfSamples\WpfSamples.sln
```

빌드 결과: 경고 0개, 오류 0개.

## 2026-05-22 RTOS Library State Update

이전 코드 리뷰에서 확인한 추가 개선 항목 중 1번부터 4번까지 반영했다.
5번 enum 파일 분리와 6번 using 정리는 현 상태를 유지하기로 했다.

1. Start/Stop/Dispose 상태 전이 보호

   `SchedulerService` 내부에 `SchedulerRunState` 상태를 추가했다.
   상태 값은 `Stopped`, `Running`, `Stopping`, `Disposed`로 구분한다.
   `Start`, `StopAsync`, `Dispose`, `DisposeAsync`, `IsRunning`에서 `_stateSyncRoot` lock을 통해 상태 접근을 보호하도록 변경했다.
   버튼 연타나 외부 코드의 동시 호출로 스케줄러 상태가 꼬일 가능성을 줄였다.

2. `ScheduledTask` 생성자 검증 추가

   `ScheduledTask`가 새로 추가한 `SchedulerTaskBase`를 상속하도록 변경했다.
   태스크 이름이 비어 있거나, 주기가 0 이하인 경우 예외를 발생시킨다.
   `executeAsync` delegate가 null인 경우 `ArgumentNullException`을 발생시킨다.

3. 실행 후보 리스트 할당 감소

   기존에는 실행 후보를 만들 때 내부 버퍼를 정렬한 뒤 `ToArray()`로 매 tick마다 배열을 새로 만들었다.
   `_executionBuffer`를 추가해 스케줄러 루프 안에서 실행 후보 목록을 재사용하도록 변경했다.
   태스크 실행 tick마다 발생하던 작은 배열 할당을 줄였다.

4. `SchedulerTaskBase` 추가

   `IScheduledTask` 구현을 표준화하기 위한 추상 기본 클래스 `SchedulerTaskBase`를 추가했다.
   `Name`, `Priority`, `Period`, `Mode`, `NextRunAt`, `IsEnabled`, `Status` 기본 구현을 제공한다.
   `NextRunAt`과 `IsEnabled` 접근은 내부 lock으로 보호한다.
   `SetEnabled(bool isEnabled)`를 제공해서 태스크 활성 상태를 안전하게 변경할 수 있게 했다.

검증:

```text
dotnet build WpfSamples\WpfSamples.sln
```

빌드 결과: 경고 0개, 오류 0개.

## 2026-05-22 RTOS Library Stability Update

`Wpf.Lib.RTOS` 라이브러리의 장시간 실행 안정성, 메모리 유지 가능성, 성능 병목 가능성을 순서대로 개선했다.

1. 이벤트 예외 보호

   `SchedulerService.SnapshotChanged` 이벤트를 직접 호출하지 않고 구독자별로 예외를 보호하도록 변경했다.
   외부 구독자에서 예외가 발생해도 스케줄러 루프가 종료되지 않도록 했다.
   추가로 `SchedulerError` 이벤트를 제공해서 태스크 실행 오류나 이벤트 구독자 오류를 외부에서 받을 수 있게 했다.

2. `StopAsync` timeout 지원

   기존 `StopAsync()`는 실행 중인 태스크가 `CancellationToken`을 무시하면 무한 대기할 수 있었다.
   `StopAsync(TimeSpan timeout)` 오버로드를 추가하고, 제한 시간 안에 멈추지 않으면 `false`를 반환하도록 변경했다.
   기존 호출부 호환을 위해 `StopAsync()`는 기존처럼 무제한 대기 동작을 유지한다.

3. 태스크 제거 API 추가

   동적으로 태스크를 추가하는 구조에서 `_tasks`, `_runtimeInfos`가 계속 참조를 유지하지 않도록 `Unregister(IScheduledTask task)`와 `Clear()`를 추가했다.
   샘플 화면이나 커스텀 컨트롤을 전환하면서 태스크를 제거할 수 있는 기반을 마련했다.

4. tick 루프 성능 개선

   매 10ms tick마다 LINQ `Where`, `OrderByDescending`, `ThenBy`, `ToList`를 수행하던 부분을 제거했다.
   내부 `_runnableBuffer`를 재사용하고 `List.Sort` 기반 비교 함수로 실행 후보를 정렬하도록 변경했다.
   실행 후보 반환 시에는 스케줄러 내부 버퍼 보호를 위해 배열 복사본을 반환한다.

5. 런타임 정보 갱신 정책 정리

   `TaskRuntimeInfo`의 setter를 외부에서 직접 수정하지 못하게 캡슐화했다.
   `MarkStarted`, `MarkCompleted`, `MarkFailed`, `ToSnapshot` 메서드를 추가했다.
   런타임 정보 갱신과 스냅샷 복사는 `_syncRoot` lock 안에서 수행하고, `task.Status` 같은 외부 태스크 속성 접근은 lock 밖에서 처리하도록 변경했다.

6. Dispose 개선

   `SchedulerService`에 `IAsyncDisposable`을 추가했다.
   비동기 정리가 필요한 경우 `DisposeAsync()`에서 먼저 정지 시도를 수행한 뒤 리소스를 정리할 수 있게 했다.
   `Dispose()`에서는 이벤트 참조를 해제해 구독자 참조가 오래 유지되는 것을 줄였다.

검증:

```text
dotnet build WpfSamples\WpfSamples.sln
```

빌드 결과: 경고 0개, 오류 0개.

## 2026-05-22 Memory Update

GUI 실행 중 메모리가 계속 증가하는 현상을 확인했다.

주요 원인:

1. `SchedulerService`가 10ms마다 `SnapshotChanged` 이벤트를 발생시켰다.
2. `MainWindowViewModel`이 매 이벤트마다 `Dispatcher.BeginInvoke`를 호출했다.
3. UI 처리 속도보다 이벤트가 빠르면 Dispatcher 큐에 갱신 작업이 누적될 수 있었다.
4. 매 갱신마다 `Tasks.Clear()` 후 `ScheduledTaskStatusViewModel`을 새로 생성해서 DataGrid 항목 churn이 컸다.

수정 내용:

1. `SchedulerService`에 표시용 `snapshotInterval`을 추가하고 기본값을 100ms로 제한했다.
2. `MainWindowViewModel`에서 UI 갱신 요청이 이미 대기 중이면 최신 스냅샷만 보관하도록 변경했다.
3. DataGrid 행 ViewModel을 매번 새로 만들지 않고 기존 `ScheduledTaskStatusViewModel`을 업데이트하도록 변경했다.

검증:

```text
dotnet build WpfSamples\WpfSamples.sln
```

빌드 결과: 경고 0개, 오류 0개.
## 2026-05-22 RTOS Library Robustness Update

`Wpf.Lib.RTOS` 코드를 다시 검토하면서 커스텀 Task 구현체가 잘못된 값을 던지는 경우와 RTOS 동기화 객체의 Dispose 이후 접근을 보강했다.

1. 스케줄러 스냅샷 보호

   `SchedulerService`가 스냅샷을 만들 때 `Status`뿐 아니라 `Name`, `Priority`, `Period`, `Mode`, `NextRunAt`, `IsEnabled` 접근도 안전하게 처리하도록 변경했다.
   커스텀 `IScheduledTask` 구현이 속성 getter에서 예외를 던져도 스케줄러 루프와 UI 스냅샷 갱신이 중단되지 않도록 기본값을 사용한다.

2. 실행 후보 정렬/판단 보호

   실행 후보를 고르고 정렬할 때도 Task 속성 접근을 보호했다.
   잘못된 Task 구현체 하나가 전체 스케줄러 tick 루프를 중단시키는 위험을 줄였다.

3. `RtosSemaphore` 상태 일관성 개선

   별도 `_currentCount`를 직접 관리하던 구조를 제거하고 `SemaphoreSlim.CurrentCount`를 기준으로 값을 제공하도록 변경했다.
   `Release()` 도중 예외가 발생했을 때 내부 카운트와 실제 semaphore 카운트가 어긋날 수 있는 가능성을 제거했다.
   Dispose 이후 `CurrentCount`, `WaitAsync`, `Release` 접근은 `ObjectDisposedException`으로 명확히 차단한다.

4. `RtosMessageQueue` Dispose 보호

   Dispose 상태 플래그를 추가하고 `Count`, `SendAsync`, `ReceiveAsync`에서 Dispose 이후 접근을 차단했다.
   큐 변경과 semaphore release를 같은 lock 구간에서 처리해 Dispose와의 경합 중 중간 상태가 노출될 가능성을 줄였다.

5. 테스트 추가

   WpfSamples의 RTOS Tests에서 다음 항목을 추가했다.

   - `Semaphore throws after dispose`
   - `MessageQueue throws after dispose`
   - `Scheduler snapshot survives faulty task properties`

검증:

```text
dotnet build WpfSamples\WpfSamples.sln
```

빌드 결과: 경고 0개, 오류 0개.
## 2026-05-22 RTOS Behavior Test Expansion Update

실제 RTOS와 유사한 동작을 더 명확히 확인하기 위해 테스트 항목을 보강하고, 함께 발견한 작은 방어 코드를 수정했다.

1. RTOS 동작 관련 테스트 추가

   WpfSamples의 RTOS Tests에서 다음 항목을 추가했다.

   - `Scheduler runs higher priority task first`
   - `Scheduler executes one-shot task once`
   - `Scheduler stop timeout returns false`
   - `Scheduler overrun task does not overlap`

   현재 스케줄러가 선점형이 아니라 cooperative 방식이라는 점을 테스트로 명확히 남겼다.
   특히 overrun 테스트는 긴 실행 시간을 가진 태스크가 중첩 실행되지 않는지 확인한다.

2. RTOS primitive 테스트 추가

   다음 항목을 추가했다.

   - `Semaphore rejects over-release`
   - `MessageQueue receive timeout`
   - `TickCounter rejects overflow tick`

3. 코드 보완

   `RtosMessageQueue<T>`의 dispose 상태 확인을 lock 기반으로 정리했다.
   `RtosTickCounter.GetTimeForTick`은 tick 값이 너무 커서 `TimeSpan` 또는 `DateTimeOffset` 범위를 넘는 경우 `ArgumentOutOfRangeException`으로 명확히 차단하도록 변경했다.

검증:

```text
dotnet build WpfSamples\WpfSamples.sln
```

빌드 결과: 경고 0개, 오류 0개.
## 2026-05-22 RTOS Overrun and Reliability Update

실제 RTOS 동작과 더 비슷하게 비교할 수 있도록 overrun 정책을 추가하고, 스케줄러/primitive 테스트를 보강했다.

1. Overrun 정책 추가

   `Enum_TaskOverrunPolicy`를 추가했다.

   - `FixedRate`: 기존 동작과 동일하게 원래 예정 시각 기준으로 다음 실행 시각을 계산한다.
   - `FixedDelay`: 태스크 완료 시각 기준으로 다음 실행 시각을 계산한다.
   - `SkipMissedTicks`: 태스크가 오래 걸려 놓친 주기를 건너뛰고 완료 시각 이후의 다음 주기로 이동한다.

   `IScheduledTask`에 `OverrunPolicy` 속성을 추가했고, `SchedulerTaskBase`와 `ScheduledTask`는 기본값을 `FixedRate`로 제공한다.
   기존 샘플 태스크인 `CounterTask`, `ClockTask`, `UiRefreshTask`도 `FixedRate` 정책을 명시했다.

2. Dispose 안정성 보강

   `SchedulerService.Dispose()`에서 실행 루프가 아직 살아 있을 때 `CancellationTokenSource`를 즉시 dispose하지 않고, 실행 루프가 끝난 뒤 dispose하도록 continuation을 연결했다.
   장시간 실행 중인 태스크가 있을 때 정리 타이밍이 꼬일 가능성을 줄였다.

3. 테스트 추가

   WpfSamples의 RTOS Tests에 다음 항목을 추가했다.

   - `Semaphore wait observes cancellation`
   - `MessageQueue preserves FIFO order`
   - `Scheduler stop honors cancellation`
   - `Scheduler unregister prevents future execution`
   - `Scheduler clear removes all tasks`
   - `Scheduler reports task errors`
   - `Scheduler survives snapshot handler errors`
   - `Scheduler fixed-delay overrun waits after completion`
   - `Scheduler skip-missed overrun advances schedule`

검증:

```text
dotnet build WpfSamples\WpfSamples.sln
```

빌드 결과: 경고 0개, 오류 0개.
## 2026-05-22 RTOS Simulation Primitives Update

Windows C# WPF 환경에서 실제 RTOS처럼 완전히 동작할 수는 없지만, 학습/시각화/검증용으로 흉내낼 수 있는 RTOS 기능을 추가했다.

1. Event Flags

   `RtosEventFlags`를 추가했다.
   `WaitAnyAsync`, `WaitAllAsync`, `Set`, `Clear`를 제공한다.
   여러 bit 이벤트 중 하나 또는 전체가 설정될 때까지 대기하는 RTOS event flag 동작을 흉내낸다.

2. Mutex와 priority inheritance 흉내

   `RtosMutex`를 추가했다.
   owner를 추적하고, 높은 우선순위 대기자가 있으면 `EffectiveOwnerPriority`로 소유자의 임시 상승 우선순위를 확인할 수 있게 했다.
   실제 OS 스케줄러의 priority inheritance는 아니지만 RTOS priority inversion 개념을 관찰할 수 있다.

3. Software Timer

   `RtosSoftwareTimer`를 추가했다.
   one-shot timer와 periodic timer를 지원한다.
   내부적으로는 `Task.Delay`를 사용하므로 하드 실시간 timer는 아니지만 RTOS software timer 개념을 테스트할 수 있다.

4. Trace Log

   `RtosTraceLog`와 `RtosTraceEntry`를 추가했다.
   `SchedulerService.TraceLog`에서 태스크 등록, 스케줄러 시작/정지, 태스크 실행 시작/완료/실패 기록을 확인할 수 있다.

5. Runtime Statistics

   `TaskRuntimeInfo`와 `ScheduledTaskSnapshot`에 실행 통계를 추가했다.

   - `MinDuration`
   - `MaxDuration`
   - `AverageDuration`
   - `LastStartDelay`
   - `MaxStartDelay`
   - `DeadlineMissCount`

6. 테스트 추가

   WpfSamples의 RTOS Tests에 다음 항목을 추가했다.

   - `EventFlags wait-any completes when flag is set`
   - `EventFlags wait-all waits for all flags`
   - `Mutex tracks owner and priority inheritance`
   - `SoftwareTimer one-shot fires once`
   - `SoftwareTimer periodic fires repeatedly`
   - `Scheduler records trace entries`
   - `Scheduler snapshot contains runtime statistics`

검증:

```text
dotnet build WpfSamples\WpfSamples.sln
```

빌드 결과: 경고 0개, 오류 0개.
## 2026-05-22 RTOS Stability Fix Update

코드 리뷰에서 확인한 취약점을 수정했다.

1. `RtosEventFlags` cancellation/timeout 정리 보강

   `CancellationToken.Register` 반환값을 보관하고, 대기 완료/취소 시 registration과 timeout CTS를 정리하도록 변경했다.
   timeout 값은 waiter 등록 전에 검증하도록 변경했다.
   외부 cancellation과 timeout cancellation 모두 대기자 목록에서 waiter를 제거하도록 정리했다.

2. `RtosSoftwareTimer.Dispose()` 안정성 보강

   실행 중인 timer task가 있을 때 `CancellationTokenSource`를 즉시 dispose하지 않고 task 완료 후 dispose하도록 변경했다.
   fire-and-forget dispose 경로에서도 task exception을 관찰해 unobserved exception 가능성을 줄였다.

3. `SchedulerService.Dispose()` CTS 정리 책임 단일화

   실행 중인 scheduler loop가 있는 경우 `RunAsync` 종료 경로의 cleanup에서 CTS를 정리하도록 변경했다.
   dispose 경로와 cleanup continuation이 같은 CTS를 동시에 정리할 수 있는 가능성을 줄였다.

4. Runtime statistics overflow 방어

   `_totalDurationTicks` 누적 시 `long.MaxValue`로 clamp하도록 변경했다.
   `RunCount`도 `long.MaxValue` 이상 증가하지 않도록 방어했다.

5. `RtosMessageQueue<T>` Send rollback 보강

   `_spaces.WaitAsync` 성공 이후 enqueue/release 과정에서 예외가 발생하면 dispose 중이 아닌 경우 space count를 복구하도록 변경했다.

6. 테스트 추가

   WpfSamples의 RTOS Tests에 다음 항목을 추가했다.

   - `EventFlags wait observes timeout`
   - `EventFlags wait observes cancellation`
   - `SoftwareTimer dispose does not throw while callback runs`

검증:

```text
dotnet build WpfSamples\WpfSamples.sln
```

빌드 결과: 경고 0개, 오류 0개.
## 2026-05-23 Dining Philosophers Demo Update

WPF 샘플 UI에 `철학자의 식사` 탭을 추가했다.

추가 내용:

1. 5명의 철학자 상태 카드
   - 생각 중
   - 배고픔
   - 식사 중
   - 식사 횟수
   - 대기 횟수
   - 왼쪽/오른쪽 포크 확보 상태

2. 5개 포크 점유 상태 표시
   - 사용 가능
   - 사용 중
   - 현재 소유 철학자

3. RTOS semaphore 기반 동작
   - 포크는 `RtosSemaphore(1, 1)`로 표현했다.
   - deadlock 방지를 위해 waiter 역할의 `RtosSemaphore(4, 4)`를 추가했다.
   - 동시에 포크 획득을 시도하는 철학자를 4명으로 제한해 원형 대기 상태를 피한다.

4. UI 제어
   - 시작
   - 중지
   - 초기화
   - 경과 시간
   - 요약 상태
   - 실시간 이벤트 로그

검증:

```text
dotnet build WpfSamples\WpfSamples.sln
dotnet run --project WpfSamples.Tests.Cli\WpfSamples.Tests.Cli.csproj
```

결과:

```text
build: 경고 0개, 오류 0개
tests: 3/3 passed
```

## 2026-05-23 Dining Philosophers Monitor/Fairness Update

철학자의 식사 데모를 RTOS Monitor 하단의 전체 Task 목록에도 표시되도록 보정했다.

변경 내용:

1. Dining philosopher 5명을 monitor용 task snapshot으로 변환
   - `Dining 철학자 1` ~ `Dining 철학자 5`
   - 상태와 상세 진행 상황을 `Status`로 표시
   - 식사 횟수를 `Runs`로 표시

2. 대기 횟수 의미 수정
   - 기존: semaphore 재시도 timeout마다 누적되어 특정 철학자만 크게 튈 수 있었다.
   - 변경: 배고픔/대기 구간에 진입한 횟수로 1회만 누적한다.

3. 포크 점유 상한 조정
   - 기존 waiter semaphore: `RtosSemaphore(4, 4)`
   - 변경 waiter semaphore: `RtosSemaphore(2, 2)`
   - 동시에 식사를 준비하는 철학자를 2명으로 제한해 포크 5개가 모두 점유된 것처럼 보이는 상태를 줄이고, 일반적으로 2명이 식사하면 포크 4개가 사용 중으로 보이게 했다.

검증:

```text
dotnet build WpfSamples\WpfSamples.sln
dotnet run --project WpfSamples.Tests.Cli\WpfSamples.Tests.Cli.csproj
```

결과:

```text
build: 경고 0개, 오류 0개
tests: 3/3 passed
```

## 2026-05-23 RTOS Library Demo Ownership Update

철학자의 식사 데모와 기본 샘플 task의 실행 책임을 `Wpf.Lib.RTOS`로 이동했다.

변경 내용:

1. `Wpf.Lib.RTOS/DiningPhilosophersDemo.cs`
   - 내부에서 `SchedulerService`를 사용한다.
   - 철학자 5명은 각각 `SchedulerTaskBase` 기반 task로 등록된다.
   - 포크는 `RtosSemaphore(1, 1)`로 관리한다.
   - waiter는 `RtosSemaphore(2, 2)`로 관리한다.
   - UI용 상태는 `DiningPhilosophersSnapshot`으로 제공한다.
   - RTOS Monitor용 task 상태는 내부 scheduler의 `SchedulerSnapshotChanged` 이벤트로 제공한다.

2. `Wpf.Lib.RTOS/SampleTasks.cs`
   - `CounterTask`
   - `ClockTask`
   - `UiRefreshTask`
   - 기존 WPF 샘플 프로젝트의 task 구현을 라이브러리로 이동했다.

3. `WpfSamples`
   - 철학자의 식사 동작을 직접 실행하지 않는다.
   - 라이브러리 snapshot을 받아 UI 컬렉션에 표시한다.
   - RTOS Monitor 하단 task 목록도 라이브러리 scheduler snapshot에서 받은 task 상태를 표시한다.

4. `SchedulerService.GetSnapshot()`
   - 현재 등록된 task 목록과 실행 상태를 라이브러리에서 직접 조회할 수 있도록 공개 API를 추가했다.

검증:

```text
dotnet build WpfSamples\WpfSamples.sln
dotnet run --project WpfSamples.Tests.Cli\WpfSamples.Tests.Cli.csproj
```

결과:

```text
build: 경고 0개, 오류 0개
tests: 3/3 passed
```
