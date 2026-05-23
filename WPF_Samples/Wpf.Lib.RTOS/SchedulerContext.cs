namespace Wpf.Lib.RTOS
{
    /// <summary>
    /// 역할: 태스크 실행 시 전달되는 컨텍스트 정보입니다.
    /// </summary>
    public sealed class SchedulerContext
    {
        private readonly Func<bool>? _shouldYield;
        private bool _isPreempted;

        /// <summary>
        /// 역할: 컨텍스트를 생성합니다.
        /// </summary>
        /// <param name="now">스케줄러가 판단한 현재 시각입니다.</param>
        /// <param name="shouldYield">더 높은 우선순위 태스크에게 양보해야 하는지 판단하는 콜백입니다.</param>
        public SchedulerContext(DateTimeOffset now, Func<bool>? shouldYield = null)
        {
            Now = now;
            _shouldYield = shouldYield;
        }

        /// <summary>
        /// 역할: 스케줄러 기준 현재 시각입니다.
        /// </summary>
        public DateTimeOffset Now { get; }

        /// <summary>
        /// 역할: 더 높은 우선순위 태스크가 runnable이면 true를 반환합니다.
        /// </summary>
        public bool ShouldYield()
        {
            return _shouldYield?.Invoke() ?? false;
        }

        /// <summary>
        /// 역할: 현재 실행 조각이 높은 우선순위 태스크에 의해 선점되었음을 표시합니다.
        /// </summary>
        public void MarkPreempted()
        {
            _isPreempted = true;
        }

        internal bool IsPreempted => _isPreempted;
    }
}
