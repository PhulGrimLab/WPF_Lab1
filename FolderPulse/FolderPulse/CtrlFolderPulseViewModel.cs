using Lib.CommonDef;
using System.Windows.Input;
using Microsoft.WindowsAPICodePack.Dialogs;
using System.IO;
using System.Net.Http.Headers;

namespace FolderPulse
{
    public class CtrlFolderPulseViewModel : ViewModelBase, IDisposable
    {
        private sealed class FolderSnapshot
        { 
            public required Dictionary<string, FileSnapshot> Files { get; init; }
            public long TotalSizeBytes { get; init; }
            public int SubFolderCount { get; init; }
        }

        private sealed class FileSnapshot
        { 
            public long SizeBytes { get; init; }
            public DateTime LastWriteTimeUtc { get; init; }
        }

        // 중앙 타이머 - 설정한 인터벌에 맞춰서 UI 갱신을 주로 수행함.
        private readonly TimeSpan _pollInterval;
        private CancellationTokenSource? _cts = new CancellationTokenSource();

        private double _interval = 1;

        private bool _disposed = false;

        // 폴더 변화 인터벌 기능
        private Task? _monitoringTask;
        private FolderSnapshot? _previousSnapshot;
        private bool _isMonitoring = false;

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

        private string _tb_FileCount = "-------------";
        public string Tb_FileCount
        {
            get => _tb_FileCount;
            set => SetProperty(ref _tb_FileCount, value);
        }


        public ICommand Btn_ExplorerCMD { get; }
        public ICommand Btn_ActionCMD { get; }

        public CtrlFolderPulseViewModel() 
        {
            Btn_ExplorerCMD = new RelayCommand(ExecuteExplorerCMD);
            Btn_ActionCMD = new RelayCommand(ExecuteActionCMD);

            _pollInterval = TimeSpan.FromSeconds(_interval);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                // 현재 _cts를 로컬 변수로 가져오고, 필드를 null로 변경
                var cts = _cts;
                _cts = null;

                if (cts != null)
                {
                    try
                    {
                        // 대기 중인 작업을 취소 시도
                        cts.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                        // 이미 Dispose된 상태라면 무시
                    }
                    finally
                    {
                        // 반드시 Dispose 호출
                        cts.Dispose();
                    }
                }
            }
            _disposed = true;
        }

        private void ExecuteActionCMD(object? parameter)
        {
            string SelectedItemName = string.Empty;

            try
            {
                if (_isMonitoring)
                {
                    StopMonitoring();
                    return;
                }

                StartMonitoring();

            }
            catch (Exception ex)
            {
                LogEx.Error($"[ERROR] ExecuteActionCMD()-Exception: {ex.ToString()}");
            }
        }

        private void StartMonitoring()
        {
            if (string.IsNullOrWhiteSpace(Tb_SelectedFolderPath))
            {
                Tb_ActivatedFolder = "폴더를 먼저 선택하세요.";
                return;
            }

            if (!Directory.Exists(Tb_SelectedFolderPath))
            {
                Tb_ActivatedFolder = "선택한 폴더가 존재하지 않습니다.";
                return;
            }

            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            Tb_ActivatedFolder = Tb_SelectedFolderPath;
            Tb_MonitoringInterval = $"{_pollInterval.TotalSeconds:N1} sec";

            _previousSnapshot = CaptureFolderSnapshot(Tb_ActivatedFolder);
            ApplySnapshotToUi(_previousSnapshot);

            _isMonitoring = true;
            _monitoringTask = RunMonitoringLoopAsync(_cts.Token);
        }

        private void StopMonitoring()
        {
            _isMonitoring = false;

            try
            {
                _cts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            Tb_MonitoringInterval = "Stopped";
        }

        private async Task RunMonitoringLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_pollInterval, token);

                    FolderSnapshot currentSnapshot = CaptureFolderSnapshot(Tb_ActivatedFolder);
                    ApplySnapshotChanges(_previousSnapshot, currentSnapshot);

                    _previousSnapshot = currentSnapshot;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogEx.Error($"[ERROR] RunMonitoringLoopAsync()-Exception: {ex}");
                }
            }
        }

        private static FolderSnapshot CaptureFolderSnapshot(string folderPath)
        {
            Dictionary<string, FileSnapshot> files = new Dictionary<string, FileSnapshot>(StringComparer.OrdinalIgnoreCase);
            long totalSizeBytes = 0;

            foreach (string filePath in Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories))
            { 
                FileInfo fileInfo = new FileInfo(filePath);

                files[filePath] = new FileSnapshot
                {
                    SizeBytes = fileInfo.Length,
                    LastWriteTimeUtc = fileInfo.LastWriteTimeUtc
                };

                totalSizeBytes += fileInfo.Length;
            }

            int subFolderCount = Directory.EnumerateDirectories(folderPath, "*", SearchOption.AllDirectories).Count();

            return new FolderSnapshot
            {
                Files = files,
                TotalSizeBytes = totalSizeBytes,
                SubFolderCount = subFolderCount
            };
        }

        private void ApplySnapshotChanges(FolderSnapshot? previous, FolderSnapshot current)
        {
            if (previous == null)
            {
                ApplySnapshotToUi(current);
                return;
            }

            int createdFileCount = current.Files.Keys.Count(path => !previous.Files.ContainsKey(path));
            int deletedFileCount = previous.Files.Keys.Count(path => !current.Files.ContainsKey(path));

            int changedFileCount = current.Files.Count(pair => 
            {
                if (!previous.Files.TryGetValue(pair.Key, out FileSnapshot? oldFlie))
                { 
                    return false;
                }

                FileSnapshot newFile = pair.Value;

                return (oldFlie.SizeBytes != newFile.SizeBytes) ||
                        (oldFlie.LastWriteTimeUtc != newFile.LastWriteTimeUtc);
            });

            long sizeDelta = current.TotalSizeBytes - previous.TotalSizeBytes;

            Tb_FolderSize = $"{FormatBytes(current.TotalSizeBytes)} ({sizeDelta:+#,##0;-#,##0;0} bytes)";
            Tb_SubFolderCount = current.SubFolderCount.ToString("N0");
            Tb_FileCount = current.Files.Count.ToString("N0");

            LogEx.T ($"[FolderPulse] Created={createdFileCount}, Deleted={deletedFileCount}, Changed={changedFileCount}, SizeDelta={sizeDelta}");
        }

        private void ApplySnapshotToUi(FolderSnapshot snapshot)
        {
            Tb_FolderSize = FormatBytes(snapshot.TotalSizeBytes);
            Tb_SubFolderCount = snapshot.SubFolderCount.ToString("N0");
            Tb_FileCount = snapshot.Files.Count.ToString("N0");
        }

        private static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = bytes;
            int unitIndex = 0;

            while (value >= 1024 && unitIndex < units.Length - 1)
            {
                value /= 1024;
                unitIndex++;
            }

            return $"{value:N2} {units[unitIndex]}";
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
