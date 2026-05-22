using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Wpf.Lib.RTOS
{
    public sealed class SchedulerContext
    {
        private readonly Func<bool>? _shouldYield;

        public SchedulerContext(DateTimeOffset now, Func<bool>? shouldYield = null)
        {
            Now = now;
            _shouldYield = shouldYield;
        }

        public DateTimeOffset Now { get; }

        public bool ShouldYield()
        {
            return _shouldYield?.Invoke() ?? false;
        }
    }
}
