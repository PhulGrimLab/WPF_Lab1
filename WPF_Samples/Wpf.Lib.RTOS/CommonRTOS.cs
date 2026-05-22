using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Wpf.Lib.RTOS
{
    public enum Enum_TaskPriority
    {
        Low = 0,
        Normal = 1,
        High = 2,
        Critical = 3
    }

    public enum Enum_TaskExecutionMode
    {
        Periodic,
        OneShot
    }

    internal class CommonRTOS
    {
    }
}
