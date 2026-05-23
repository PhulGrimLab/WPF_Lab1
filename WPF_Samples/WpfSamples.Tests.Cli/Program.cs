using WpfSamples.Tests;
using System.Windows.Threading;

var cmdArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();

if (cmdArgs.Contains("--compare-quantum", StringComparer.OrdinalIgnoreCase))
{
    await RunQuantumComparisonAsync().ConfigureAwait(false);
    return;
}

if (cmdArgs.Contains("--all", StringComparer.OrdinalIgnoreCase))
{
    await RunAllTestsAsync().ConfigureAwait(false);
    return;
}

var selectedTests = ParseSelectedTests(cmdArgs);
if (selectedTests is not null)
{
    await RunSelectedTestsAsync(selectedTests, repeat: 1).ConfigureAwait(false);
    return;
}

var repeat = ParseRepeat(cmdArgs);

var testNames = new[]
{
    "SchedulerCooperativePreemptionYieldsToHigherPriorityAsync",
    "SchedulerPreemptedTaskResumesImmediatelyAsync",
    "SchedulerTimeQuantumPreemptsLongTaskAsync",
};
await RunSelectedTestsAsync(testNames, repeat).ConfigureAwait(false);

static int ParseRepeat(string[] args)
{
    var repeatArgIndex = Array.FindIndex(args, arg => string.Equals(arg, "--repeat", StringComparison.OrdinalIgnoreCase));
    if (repeatArgIndex < 0 || repeatArgIndex == args.Length - 1)
    {
        return 1;
    }

    return int.TryParse(args[repeatArgIndex + 1], out var repeat) && repeat > 0
        ? repeat
        : 1;
}

static IReadOnlyList<string>? ParseSelectedTests(string[] args)
{
    var testsArgIndex = Array.FindIndex(args, arg => string.Equals(arg, "--tests", StringComparison.OrdinalIgnoreCase));
    if (testsArgIndex < 0 || testsArgIndex == args.Length - 1)
    {
        return null;
    }

    var names = args[testsArgIndex + 1]
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    return names.Length == 0 ? null : names;
}

static async Task RunSelectedTestsAsync(IReadOnlyList<string> testNames, int repeat)
{
    var runnerType = typeof(RtosTestRunner);
    var failed = 0;

    for (var iteration = 1; iteration <= repeat; iteration++)
    {
        Console.WriteLine($"Run {iteration}/{repeat}");

        foreach (var testName in testNames)
        {
            var method = runnerType.GetMethod(testName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (method is null)
            {
                Console.WriteLine($"[FAIL] {testName} - test method was not found.");
                failed++;
                continue;
            }

            var startedAt = DateTimeOffset.Now;

            try
            {
                var invoked = method.Invoke(null, null);
                if (invoked is not Task task)
                {
                    Console.WriteLine($"[FAIL] {testName} - test method did not return Task.");
                    failed++;
                    continue;
                }

                var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(15))).ConfigureAwait(false);
                if (completed != task)
                {
                    Console.WriteLine($"[FAIL] {testName} - timeout after 15 seconds.");
                    failed++;
                    continue;
                }

                await task.ConfigureAwait(false);
                Console.WriteLine($"[PASS] {testName} ({(DateTimeOffset.Now - startedAt).TotalMilliseconds:N0} ms)");
            }
            catch (Exception ex)
            {
                var root = ex is System.Reflection.TargetInvocationException tie && tie.InnerException is not null
                    ? tie.InnerException
                    : ex;
                Console.WriteLine($"[FAIL] {testName} - {root.Message}");
                failed++;
            }
        }
    }

    Console.WriteLine($"Summary: {testNames.Count * repeat - failed}/{testNames.Count * repeat} passed");
    Environment.ExitCode = failed == 0 ? 0 : 1;
}

static async Task RunQuantumComparisonAsync()
{
    var targetMethodName = "SchedulerTimeQuantumPreemptsLongTaskAsync";
    var runnerType = typeof(RtosTestRunner);
    var method = runnerType.GetMethod(targetMethodName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
    if (method is null)
    {
        Console.WriteLine($"[FAIL] {targetMethodName} - test method was not found.");
        Environment.ExitCode = 1;
        return;
    }

    var quantums = new[] { 3, 4, 5 };
    var iterations = 30;
    var failures = 0;

    Console.WriteLine("Quantum Comparison (SchedulerTimeQuantumPreemptsLongTaskAsync)");
    Console.WriteLine($"Iterations per quantum: {iterations}");

    foreach (var quantumMs in quantums)
    {
        Environment.SetEnvironmentVariable("RTOS_TEST_TIME_QUANTUM_MS", quantumMs.ToString());

        var passed = 0;
        var elapsed = TimeSpan.Zero;

        for (var i = 0; i < iterations; i++)
        {
            var startedAt = DateTimeOffset.Now;

            try
            {
                var invoked = method.Invoke(null, null);
                if (invoked is not Task task)
                {
                    failures++;
                    continue;
                }

                var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(15))).ConfigureAwait(false);
                if (completed != task)
                {
                    failures++;
                    continue;
                }

                await task.ConfigureAwait(false);
                passed++;
                elapsed += DateTimeOffset.Now - startedAt;
            }
            catch
            {
                failures++;
            }
        }

        var average = passed > 0 ? elapsed.TotalMilliseconds / passed : double.NaN;
        Console.WriteLine($"Quantum {quantumMs}ms: pass {passed}/{iterations}, avg {average:N2} ms");
    }

    Environment.SetEnvironmentVariable("RTOS_TEST_TIME_QUANTUM_MS", null);
    Environment.ExitCode = failures == 0 ? 0 : 1;
}

static async Task RunAllTestsAsync()
{
    var results = await RunOnStaAsync(() => RtosTestRunner.RunAllAsync()).ConfigureAwait(false);
    var passedCount = results.Count(result => result.Passed);

    Console.WriteLine($"Summary: {passedCount}/{results.Count} passed");

    foreach (var result in results)
    {
        Console.WriteLine($"[{result.Result}] {result.Name} ({result.Duration}) - {result.Message}");
    }

    Environment.ExitCode = passedCount == results.Count ? 0 : 1;
}

static async Task<T> RunOnStaAsync<T>(Func<Task<T>> work)
{
    var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

    var thread = new Thread(() =>
    {
        var dispatcher = Dispatcher.CurrentDispatcher;

        _ = dispatcher.InvokeAsync(async () =>
        {
            try
            {
                var result = await work().ConfigureAwait(true);
                completion.TrySetResult(result);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
            finally
            {
                dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            }
        });

        Dispatcher.Run();
    });

    thread.IsBackground = true;
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();

    try
    {
        return await completion.Task.ConfigureAwait(false);
    }
    finally
    {
        thread.Join();
    }
}
