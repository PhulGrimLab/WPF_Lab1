namespace WpfSamples.Tests;

internal sealed class RtosTestResult
{
    public RtosTestResult(string name, bool passed, string message, TimeSpan duration)
    {
        Name = name;
        Passed = passed;
        Result = passed ? "PASS" : "FAIL";
        Message = message;
        Duration = $"{duration.TotalMilliseconds:N2} ms";
    }

    public string Name { get; }
    public bool Passed { get; }
    public string Result { get; }
    public string Message { get; }
    public string Duration { get; }
}
