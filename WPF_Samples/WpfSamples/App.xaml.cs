using System.IO;
using System.Text;
using System.Windows;
using WpfSamples.Tests;

namespace WpfSamples
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (e.Args.Any(argument => string.Equals(argument, "--run-rtos-tests", StringComparison.OrdinalIgnoreCase)))
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var exitCode = await RunRtosTestsCliAsync().ConfigureAwait(true);
                Shutdown(exitCode);
                return;
            }

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
        }

        private static async Task<int> RunRtosTestsCliAsync()
        {
            var outputPath = Path.Combine(Directory.GetCurrentDirectory(), "rtos-test-results.txt");

            try
            {
                var results = await RtosTestRunner.RunAllAsync().ConfigureAwait(false);
                var passedCount = results.Count(result => result.Passed);

                var builder = new StringBuilder();
                builder.AppendLine($"Summary: {passedCount}/{results.Count} passed");
                builder.AppendLine($"GeneratedAt: {DateTimeOffset.Now:O}");
                builder.AppendLine();

                foreach (var result in results)
                {
                    builder.AppendLine($"[{(result.Passed ? "PASS" : "FAIL")}] {result.Name} ({result.Duration}) - {result.Message}");
                }

                await File.WriteAllTextAsync(outputPath, builder.ToString()).ConfigureAwait(false);
                return passedCount == results.Count ? 0 : 1;
            }
            catch (Exception ex)
            {
                var failureLog = $"CLI test run failed: {ex}{Environment.NewLine}";
                await File.WriteAllTextAsync(outputPath, failureLog).ConfigureAwait(false);
                return 2;
            }
        }
    }

}
