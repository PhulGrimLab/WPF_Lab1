using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Wpf.Lib.RTOS
{
    public interface IScheduledTask
    {
        string Name { get; }
        Enum_TaskPriority Priority { get; }
        TimeSpan Period { get; }
        Enum_TaskExecutionMode Mode { get; }
        Enum_TaskOverrunPolicy OverrunPolicy { get; }

        DateTimeOffset NextRunAt { get; set; }
        bool IsEnabled { get; }

        string Status { get; }

        Task ExecuteAsync(SchedulerContext context, CancellationToken cancellationToken);
    }
}
