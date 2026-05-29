using Lib.CommonDef;
using System.Windows.Input;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace FolderPulse
{
    public class CtrlFolderPulseViewModel : ViewModelBase
    {
        private string _tb_SelectedFolderPath = string.Empty;
        public string Tb_SelectedFolderPath
        {
            get => _tb_SelectedFolderPath;
            set => SetProperty(ref _tb_SelectedFolderPath, value);
        }

        private string _tb_ActivatedFolder = "-------------";
        public string Tb_ActivatedFolder
        {
            get => _tb_ActivatedFolder;
            set => SetProperty(ref _tb_ActivatedFolder, value);
        }

        private string _tb_MonitoringInterval = "-------------";
        public string Tb_MonitoringInterval
        {
            get => _tb_MonitoringInterval;
            set => SetProperty(ref _tb_MonitoringInterval, value);
        }

        private string _tb_FolderSize = "-------------";
        public string Tb_FolderSize
        {
            get => _tb_FolderSize;
            set => SetProperty(ref _tb_FolderSize, value);
        }

        private string _tb_SubFolderCount = "-------------";
        public string Tb_SubFolderCount
        {
            get => _tb_SubFolderCount;
            set => SetProperty(ref _tb_SubFolderCount, value);
        }


        public ICommand Btn_ExplorerCMD { get; }
        public ICommand Btn_ActionCMD { get; }

        public CtrlFolderPulseViewModel() 
        {
            Btn_ExplorerCMD = new RelayCommand(ExecuteExplorerCMD);
            Btn_ActionCMD = new RelayCommand(ExecuteActionCMD);
        }

        private void ExecuteActionCMD(object? parameter)
        {
            string SelectedItemName = string.Empty;

            try
            {
                // 선택된 폴더 경로를 활성화 폴더 경로로 지정
                Tb_ActivatedFolder = Tb_SelectedFolderPath;
            }
            catch (Exception ex)
            {
                LogEx.Error($"[ERROR] ExecuteActionCMD()-Exception: {ex.ToString()}");
            }
        }

        private void ExecuteExplorerCMD(object? parameter)
        {
            string SelectedItemName = string.Empty;

            try
            {
                CommonOpenFileDialog cofd = new CommonOpenFileDialog();

                // 파일 선택이 아닌 폴더 선택하도록 설정
                cofd.IsFolderPicker = true;

                if ((cofd.ShowDialog() == CommonFileDialogResult.Ok) && 
                    (cofd.FileName != null))        // 이름은 FileName에서 가져와야 함.
                {
                    Tb_SelectedFolderPath = cofd.FileName;
                }
            
            }
            catch (Exception ex)
            {
                LogEx.Error($"[ERROR] ExecuteExplorerCMD()-Exception: {ex.ToString()}");
            }
        }
    }
}
