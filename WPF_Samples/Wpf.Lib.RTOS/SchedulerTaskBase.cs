namespace Wpf.Lib.RTOS
{
    public abstract class SchedulerTaskBase : IScheduledTask
    {
        private readonly object _syncRoot = new();
        private DateTimeOffset _nextRunAt;
        private bool _isEnabled = true;

        protected SchedulerTaskBase(
            string name,
            Enum_TaskPriority priority,
            TimeSpan period,
            Enum_TaskExecutionMode mode)
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
            _nextRunAt = DateTimeOffset.Now;
        }

        public string Name { get; }
        public Enum_TaskPriority Priority { get; }
        public TimeSpan Period { get; }
        public Enum_TaskExecutionMode Mode { get; }

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

        public virtual string Status => string.Empty;

        public void SetEnabled(bool isEnabled)
        {
            lock (_syncRoot)
            {
                _isEnabled = isEnabled;
            }
        }

        public abstract Task ExecuteAsync(SchedulerContext context, CancellationToken cancellationToken);
    }
}
