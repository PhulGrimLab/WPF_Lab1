namespace Wpf.Lib.RTOS
{
    /// <summary>
    /// 공통 태스크 속성과 동기화 처리를 제공하는 베이스 클래스입니다.
    /// </summary>
    public abstract class SchedulerTaskBase : IScheduledTask
    {
        private readonly object _syncRoot = new();
        private DateTimeOffset _nextRunAt;
        private bool _isEnabled = true;

        /// <summary>
        /// 기본 태스크를 생성합니다.
        /// </summary>
        /// <param name="name">태스크 이름입니다.</param>
        /// <param name="priority">우선순위입니다.</param>
        /// <param name="period">주기입니다.</param>
        /// <param name="mode">실행 모드입니다.</param>
        /// <param name="overrunPolicy">오버런 시 다음 실행 시각 계산 정책입니다.</param>
        protected SchedulerTaskBase(
            string name,
            Enum_TaskPriority priority,
            TimeSpan period,
            Enum_TaskExecutionMode mode,
            Enum_TaskOverrunPolicy overrunPolicy = Enum_TaskOverrunPolicy.FixedRate)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Task name must not be empty.", nameof(name));
            }

            if (period <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(period), "Task period must be greater than zero.");
            }

            Name = name;
            Priority = priority;
            Period = period;
            Mode = mode;
            OverrunPolicy = overrunPolicy;
            _nextRunAt = DateTimeOffset.Now;
        }

        /// <summary>
        /// 태스크 이름입니다.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// 태스크 우선순위입니다.
        /// </summary>
        public Enum_TaskPriority Priority { get; }

        /// <summary>
        /// 실행 주기입니다.
        /// </summary>
        public TimeSpan Period { get; }

        /// <summary>
        /// 실행 모드입니다.
        /// </summary>
        public Enum_TaskExecutionMode Mode { get; }

        /// <summary>
        /// 오버런 정책입니다.
        /// </summary>
        public Enum_TaskOverrunPolicy OverrunPolicy { get; }

        /// <summary>
        /// 다음 실행 예정 시각입니다.
        /// </summary>
        public DateTimeOffset NextRunAt
        {
            get
            {
                lock (_syncRoot)
                {
                    return _nextRunAt;
                }
            }
            set
            {
                lock (_syncRoot)
                {
                    _nextRunAt = value;
                }
            }
        }

        /// <summary>
        /// 활성화 여부입니다.
        /// </summary>
        public bool IsEnabled
        {
            get
            {
                lock (_syncRoot)
                {
                    return _isEnabled;
                }
            }
        }

        /// <summary>
        /// UI 표시용 상태 문자열입니다.
        /// </summary>
        public virtual string Status => string.Empty;

        /// <summary>
        /// 태스크 활성 상태를 변경합니다.
        /// </summary>
        /// <param name="isEnabled">true면 활성, false면 비활성입니다.</param>
        public void SetEnabled(bool isEnabled)
        {
            lock (_syncRoot)
            {
                _isEnabled = isEnabled;
            }
        }

        /// <summary>
        /// 실제 태스크 작업을 실행합니다.
        /// </summary>
        public abstract Task ExecuteAsync(SchedulerContext context, CancellationToken cancellationToken);
    }
}
