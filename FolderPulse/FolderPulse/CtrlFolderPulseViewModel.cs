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

        private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(1);

        // 중앙 타이머 - 설정한 인터벌에 맞춰서 폴더 상태 체크를 수행함.
        private TimeSpan _pollInterval;
        private CancellationTokenSource? _cts = new CancellationTokenSource();

        private int _remainingSeconds = 1;

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

        public IReadOnlyList<int> MonitoringIntervalOptions { get; } = new List<int>
        {
            1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 15, 20
        };

        private int _selectedMonitoringIntervalSeconds = 1;
        public int SelectedMonitoringIntervalSeconds
        {
            get => _selectedMonitoringIntervalSeconds;
            set
            {
                if (SetProperty(ref _selectedMonitoringIntervalSeconds, value))
                {
                    ApplyMonitoringInterval(value);
                }
            }
        }

        private string _tb_MonitoringHeartbeat = "Heartbeat: stopped";
        public string Tb_MonitoringHeartbeat
        {
            get => _tb_MonitoringHeartbeat;
            set => SetProperty(ref _tb_MonitoringHeartbeat, value);
        }

        private bool _isMonitoringHeartbeatOn = false;
        public bool IsMonitoringHeartbeatOn
        {
            get => _isMonitoringHeartbeatOn;
            set => SetProperty(ref _isMonitoringHeartbeatOn, value);
        }

        private string _tb_MonitoringCountdown = "--";
        public string Tb_MonitoringCountdown
        {
            get => _tb_MonitoringCountdown;
            set => SetProperty(ref _tb_MonitoringCountdown, value);
        }

        private string _tb_LastCheckedTime = "Last checked: --";
        public string Tb_LastCheckedTime
        {
            get => _tb_LastCheckedTime;
            set => SetProperty(ref _tb_LastCheckedTime, value);
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

            ApplyMonitoringInterval(SelectedMonitoringIntervalSeconds);
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
            _remainingSeconds = GetIntervalSeconds();
            Tb_MonitoringCountdown = _remainingSeconds.ToString();
            Tb_MonitoringHeartbeat = "Next folder check countdown";
            IsMonitoringHeartbeatOn = true;

            _previousSnapshot = CaptureFolderSnapshot(Tb_ActivatedFolder);
            ApplySnapshotToUi(_previousSnapshot);
            UpdateLastCheckedTime();

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
            Tb_MonitoringHeartbeat = "Heartbeat: stopped";
            Tb_MonitoringCountdown = "--";
            IsMonitoringHeartbeatOn = false;
        }

        private async Task RunMonitoringLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(HeartbeatInterval, token);

                    _remainingSeconds--;

                    if (_remainingSeconds <= 0)
                    {
                        FolderSnapshot currentSnapshot = CaptureFolderSnapshot(Tb_ActivatedFolder);
                        ApplySnapshotChanges(_previousSnapshot, currentSnapshot);

                        _previousSnapshot = currentSnapshot;
                        UpdateLastCheckedTime();
                        _remainingSeconds = GetIntervalSeconds();
                        UpdateHeartbeat(_remainingSeconds, true);
                        continue;
                    }

                    UpdateHeartbeat(_remainingSeconds);
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

        private void ApplyMonitoringInterval(int seconds)
        {
            int intervalSeconds = MonitoringIntervalOptions.Contains(seconds)
                ? seconds
                : MonitoringIntervalOptions[0];

            _pollInterval = TimeSpan.FromSeconds(intervalSeconds);
            Tb_MonitoringInterval = $"{intervalSeconds} sec";

            if (!_isMonitoring)
            {
                return;
            }

            _remainingSeconds = GetIntervalSeconds();
            Tb_MonitoringCountdown = _remainingSeconds.ToString();
            Tb_MonitoringHeartbeat = $"Interval changed; next check in {_remainingSeconds} sec";
        }

        private int GetIntervalSeconds()
        {
            return Math.Max(1, (int)Math.Ceiling(_pollInterval.TotalSeconds));
        }

        private void UpdateHeartbeat(int remainingSeconds, bool checkedNow = false)
        {
            IsMonitoringHeartbeatOn = !IsMonitoringHeartbeatOn;

            Tb_MonitoringCountdown = remainingSeconds.ToString();
            Tb_MonitoringHeartbeat = checkedNow
                ? $"Checked at {DateTime.Now:HH:mm:ss}; next check in {remainingSeconds} sec"
                : $"Next folder check in {remainingSeconds} sec";
        }

        private void UpdateLastCheckedTime()
        {
            Tb_LastCheckedTime = $"Last checked: {DateTime.Now:HH:mm:ss}";
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
