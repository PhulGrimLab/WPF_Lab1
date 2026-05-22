using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Wpf.Lib.RTOS
{
    public sealed class ScheduledTask : IScheduledTask
    {
        private readonly Func<SchedulerContext, CancellationToken, Task> _executeAsync;
        private readonly Func<string>? _statusProvider;

        public ScheduledTask(
            string name,
            Enum_TaskPriority priority,
            TimeSpan period,
            Enum_TaskExecutionMode mode,
            Func<SchedulerContext, CancellationToken, Task> executeAsync,
            Func<string>? statusProvider = null)
        {
            Name = name;
            Priority = priority;
            Period = period;
            Mode = mode;
            _executeAsync = executeAsync;
            _statusProvider = statusProvider;
            NextRunAt = DateTimeOffset.Now;
        }

        public string Name { get; }
        public Enum_TaskPriority Priority { get; }
        public TimeSpan Period { get; }
        public Enum_TaskExecutionMode Mode { get; }
        public DateTimeOffset NextRunAt { get; set; }
        public bool IsEnabled { get; set; } = true;
        public string Status => _statusProvider?.Invoke() ?? string.Empty;

        public Task ExecuteAsync(SchedulerContext context, CancellationToken cancellationToken)
        {
            return _executeAsync(context, cancellationToken);
        }
    }

}
