using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Wpf.Lib.RTOS
{
    public sealed class ScheduledTask : SchedulerTaskBase
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
            : base(name, priority, period, mode)
        {
            _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
            _statusProvider = statusProvider;
        }

        public override string Status => _statusProvider?.Invoke() ?? string.Empty;

        public override Task ExecuteAsync(SchedulerContext context, CancellationToken cancellationToken)
        {
            return _executeAsync(context, cancellationToken);
        }
    }

}
