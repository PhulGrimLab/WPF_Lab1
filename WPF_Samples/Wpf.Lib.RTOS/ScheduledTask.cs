namespace Wpf.Lib.RTOS
{
    /// <summary>
    /// 역할: 람다(delegate) 기반으로 태스크를 빠르게 정의하는 구현체입니다.
    /// </summary>
    public sealed class ScheduledTask : SchedulerTaskBase
    {
        private readonly Func<SchedulerContext, CancellationToken, Task> _executeAsync;
        private readonly Func<string>? _statusProvider;

        /// <summary>
        /// 역할: 람다 기반 태스크를 생성합니다.
        /// </summary>
        /// <param name="name">태스크 이름입니다.</param>
        /// <param name="priority">실행 우선순위입니다.</param>
        /// <param name="period">실행 주기입니다.</param>
        /// <param name="mode">실행 모드(Periodic/OneShot)입니다.</param>
        /// <param name="executeAsync">실제 작업 본문 delegate입니다.</param>
        /// <param name="statusProvider">UI 표시용 상태 문자열 공급 delegate입니다.</param>
        /// <param name="overrunPolicy">주기 초과 시 다음 실행 시각 계산 정책입니다.</param>
        public ScheduledTask(
            string name,
            Enum_TaskPriority priority,
            TimeSpan period,
            Enum_TaskExecutionMode mode,
            Func<SchedulerContext, CancellationToken, Task> executeAsync,
            Func<string>? statusProvider = null,
            Enum_TaskOverrunPolicy overrunPolicy = Enum_TaskOverrunPolicy.FixedRate)
            : base(name, priority, period, mode, overrunPolicy)
        {
            _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
            _statusProvider = statusProvider;
        }

        /// <summary>
        /// 역할: 상태 제공 콜백 결과를 반환합니다.
        /// </summary>
        public override string Status => _statusProvider?.Invoke() ?? string.Empty;

        /// <summary>
        /// 역할: 등록된 실행 delegate를 호출합니다.
        /// </summary>
        /// <param name="context">현재 시각/양보 판단 컨텍스트입니다.</param>
        /// <param name="cancellationToken">중지 요청 토큰입니다.</param>
        /// <returns>태스크 본문 delegate의 비동기 실행 작업입니다.</returns>
        public override Task ExecuteAsync(SchedulerContext context, CancellationToken cancellationToken)
        {
            return _executeAsync(context, cancellationToken);
        }
    }
}
