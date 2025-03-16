using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace AsyncAwaitTest2;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private CancellationTokenSource _cancellationTokenSource;

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        // 기존 작업 취소
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource = new CancellationTokenSource();

        var progress = new Progress<double>();
        progress.ProgressChanged += (s, percent) =>
        {
            ProgressBar.Value = percent * 100; // ProgressBar 업데이트
        };

        try
        {
            // 새 작업 시작
            await MyMethodAsync(progress, _cancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            // 작업이 취소됨
            ProgressBar.Value = 0; // 진행 상태 초기화
        }
    }

    private async Task MyMethodAsync(IProgress<double> progress = null, CancellationToken cancellationToken = default)
    {
        bool done = false;
        double percentComplete = 0;

        while (!done)
        {
            await Task.Delay(100, cancellationToken); // 취소 가능한 딜레이

            percentComplete += 0.1; // 진행률 증가
            if (percentComplete >= 1.0)
            {
                percentComplete = 1.0;
                done = true;
            }

            // 진행률 보고
            progress?.Report(percentComplete);

            // 취소 확인
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}