namespace Wpf.Lib.RTOS
{
    /// <summary>
    /// 스케줄러가 실행할 태스크의 최소 계약입니다.
    /// </summary>
    public interface IScheduledTask
    {
        /// <summary>
        /// UI와 로그에 표시할 태스크 이름입니다.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 실행 우선순위입니다.
        /// </summary>
        Enum_TaskPriority Priority { get; }

        /// <summary>
        /// 실행 주기입니다.
        /// </summary>
        TimeSpan Period { get; }

        /// <summary>
        /// 반복 실행인지 1회 실행인지 나타냅니다.
        /// </summary>
        Enum_TaskExecutionMode Mode { get; }

        /// <summary>
        /// 실행 시간이 주기를 넘겼을 때 다음 실행 시각 계산 정책입니다.
        /// </summary>
        Enum_TaskOverrunPolicy OverrunPolicy { get; }

        /// <summary>
        /// 다음 실행 예정 시각입니다.
        /// </summary>
        DateTimeOffset NextRunAt { get; set; }

        /// <summary>
        /// 태스크 활성 여부입니다.
        /// </summary>
        bool IsEnabled { get; }

        /// <summary>
        /// UI에 표시할 상태 문자열입니다.
        /// </summary>
        string Status { get; }

        /// <summary>
        /// 태스크 본문을 실행합니다.
        /// </summary>
        /// <param name="context">현재 시각 및 양보 판단을 담은 실행 컨텍스트입니다.</param>
        /// <param name="cancellationToken">중지 요청 전달 토큰입니다.</param>
        Task ExecuteAsync(SchedulerContext context, CancellationToken cancellationToken);

        /// <summary>
        /// 태스크의 활성 상태를 설정합니다.
        /// </summary>
        void SetEnabled(bool isEnabled);
    }
}
