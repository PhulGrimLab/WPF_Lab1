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

namespace AsyncAwaitDeepSeek1;

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

    // Start Button Click Event Handler
    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        // 기존 작업이 실행 중인 경우 취소
        _cancellationTokenSource?.Cancel();

        // ProgressBar 초기화
        progressBar.Value = 0;

        // 새로운 CancellationTokenSource 생성
        _cancellationTokenSource = new CancellationTokenSource();

        // Progress 객체 생성
        var progress = new Progress<double>();
        progress.ProgressChanged += (s, percent) =>
        {
            // ProgressBar 업데이트 (UI 스레드에서 안전하게 실행)
            progressBar.Value = percent;
        };

        try
        {
            // 비동기 작업 실행
            await DoWorkAsync(progress, _cancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            // 작업이 취소된 경우 아무 작업도 하지 않음
        }
        finally
        {
            // 작업 완료 후 CancellationTokenSource 초기화
            _cancellationTokenSource = null;
        }
    }

    // 비동기 작업 시뮬레이션
    private async Task DoWorkAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        for (int i = 0; i <= 100; i++)
        {
            // 취소 요청이 있는지 확인
            cancellationToken.ThrowIfCancellationRequested();

            // 진행률 보고
            progress?.Report(i);

            // 작업 시뮬레이션을 위한 지연
            await Task.Delay(50, cancellationToken); // 50ms 지연 (취소 가능)
        }
    }
}