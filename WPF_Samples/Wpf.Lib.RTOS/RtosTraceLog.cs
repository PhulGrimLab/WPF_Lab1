namespace Wpf.Lib.RTOS;

public sealed class RtosTraceLog
{
    private readonly object _syncRoot = new();
    private readonly Queue<RtosTraceEntry> _entries = new();

    public RtosTraceLog(int capacity = 512)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Trace capacity must be greater than zero.");
        }

        Capacity = capacity;
    }

    public int Capacity { get; }

    public void Add(string category, string message, string? taskName = null)
    {
        var entry = new RtosTraceEntry(DateTimeOffset.Now, category, message, taskName);

        lock (_syncRoot)
        {
            while (_entries.Count >= Capacity)
            {
                _entries.Dequeue();
            }

            _entries.Enqueue(entry);
        }
    }

    public IReadOnlyList<RtosTraceEntry> Snapshot()
    {
        lock (_syncRoot)
        {
            return _entries.ToArray();
        }
    }

    public void Clear()
    {
        lock (_syncRoot)
        {
            _entries.Clear();
        }
    }
}

public sealed record RtosTraceEntry(
    DateTimeOffset CreatedAt,
    string Category,
    string Message,
    string? TaskName);
