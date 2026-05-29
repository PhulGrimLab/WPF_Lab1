using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using Lib.CommonDef;

namespace FolderPulse
{
    public class CtrlFolderPulseViewModel : ViewModelBase
    {
        public ICommand Btn_ExplorerCMD { get; } = new RelayCommand(ExecuteExplorerCMD);
    }
}
