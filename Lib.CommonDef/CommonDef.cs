using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Lib.CommonDef
{
    public class CommonDef
    {
        private static readonly Lazy<CommonDef> _singleton = new Lazy<CommonDef>(() => new CommonDef());

        public static CommonDef Instance => _singleton.Value;
    }
}
