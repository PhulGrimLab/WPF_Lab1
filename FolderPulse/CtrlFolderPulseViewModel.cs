using Lib.CommonDef;
using System.Windows.Input;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace FolderPulse
{
    public class CtrlFolderPulseViewModel : ViewModelBase
    {
        private string _tb_SelectedFolderPath;
        public string Tb_SelectedFolderPath
        {
            get => _tb_SelectedFolderPath;
            set => SetProperty(ref _tb_SelectedFolderPath, value);
        }

        public ICommand Btn_ExplorerCMD { get; }

        public CtrlFolderPulseViewModel() 
        {
            Btn_ExplorerCMD = new RelayCommand(ExecuteExplorerCMD);

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
