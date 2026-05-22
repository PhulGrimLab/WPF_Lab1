namespace Wpf.Lib.RTOS
{
    /// <summary>
    /// 스케줄러와 primitive에서 공통으로 사용하는 RTOS 기본 열거형 정의 모음입니다.
    /// </summary>
    /// <summary>
    /// 태스크 우선순위를 나타냅니다. 숫자가 클수록 먼저 실행됩니다.
    /// </summary>
    public enum Enum_TaskPriority
    {
        /// <summary>
        /// 낮은 우선순위입니다.
        /// </summary>
        Low = 0,

        /// <summary>
        /// 기본 우선순위입니다.
        /// </summary>
        Normal = 1,

        /// <summary>
        /// 높은 우선순위입니다.
        /// </summary>
        High = 2,

        /// <summary>
        /// 가장 높은 우선순위입니다.
        /// </summary>
        Critical = 3
    }

    /// <summary>
    /// 태스크 실행 방식을 나타냅니다.
    /// </summary>
    public enum Enum_TaskExecutionMode
    {
        /// <summary>
        /// 주기적으로 반복 실행합니다.
        /// </summary>
        Periodic,

        /// <summary>
        /// 한 번만 실행하고 종료합니다.
        /// </summary>
        OneShot
    }

    /// <summary>
    /// 태스크 현재 상태를 나타냅니다.
    /// </summary>
    public enum Enum_TaskState
    {
        /// <summary>
        /// 즉시 실행 가능한 상태입니다.
        /// </summary>
        Ready,

        /// <summary>
        /// 현재 실행 중입니다.
        /// </summary>
        Running,

        /// <summary>
        /// 다음 시각 또는 이벤트를 기다리는 상태입니다.
        /// </summary>
        Blocked,

        /// <summary>
        /// 비활성화되어 스케줄러가 실행하지 않는 상태입니다.
        /// </summary>
        Suspended
    }

    /// <summary>
    /// 태스크가 주기를 초과했을 때 다음 실행 시각 계산 정책입니다.
    /// </summary>
    public enum Enum_TaskOverrunPolicy
    {
        /// <summary>
        /// 원래 계획된 시각 기준으로 다음 실행 시각을 계산합니다.
        /// </summary>
        FixedRate,

        /// <summary>
        /// 실제 완료 시각 기준으로 다음 실행 시각을 계산합니다.
        /// </summary>
        FixedDelay,

        /// <summary>
        /// 놓친 주기를 건너뛰고 현재 시각 이후 주기로 이동합니다.
        /// </summary>
        SkipMissedTicks
    }
}
