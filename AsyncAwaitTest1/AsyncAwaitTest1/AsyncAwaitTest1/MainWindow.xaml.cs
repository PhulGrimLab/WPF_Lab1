using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;

namespace AsyncAwaitTest1;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private CancellationTokenSource _cts;

    public MainWindow()
    {
        InitializeComponent();
    }

    // 버튼 클릭 시, 진행 중인 작업 취소 후 새 작업 시작
    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        // 이미 작업이 진행 중이면 취소 요청
        if (_cts != null)
        {
            _cts.Cancel();
        }

        // 새 CancellationTokenSource 생성
        _cts = new CancellationTokenSource();

        // UI 초기화
        progressBar.Value = 0;
        progressLabel.Content = "Progress: 0%";

        // Progress<double> 생성: UI 스레드에서 업데이트됨
        var progressIndicator = new Progress<double>(value =>
        {
            progressBar.Value = value * 100;
            progressLabel.Content = $"Progress: {(value * 100):F0}%";
        });

        try
        {
            // 비동기 작업 실행 (취소 토큰 전달)
            await MyMethodAsync(progressIndicator, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            // 취소된 경우 UI에 알림
            progressLabel.Content = "작업이 취소되었습니다.";
        }
    }

    // 진행 상황을 업데이트하는 비동기 메서드 (CancellationToken 사용)
    private async Task MyMethodAsync(IProgress<double> progress, CancellationToken token)
    {
        bool done = false;
        double percentComplete = 0;

        // 진행 상황 업데이트 루프 (예: 시뮬레이션)
        while (!done)
        {
            // 취소 요청이 있는 경우 예외 발생
            token.ThrowIfCancellationRequested();

            // 작업 시뮬레이션: 100ms 대기
            await Task.Delay(100, token);

            // 진행율 증가 (0.01씩 증가, 1.0 즉 100%까지)
            percentComplete += 0.01;

            if (percentComplete >= 1.0)
            {
                percentComplete = 1.0;
                done = true;
            }

            // 진행 상황 보고 (UI 업데이트)
            progress?.Report(percentComplete);
        }
    }
}