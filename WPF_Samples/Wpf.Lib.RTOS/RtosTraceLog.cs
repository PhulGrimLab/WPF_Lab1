namespace Wpf.Lib.RTOS;

/// <summary>
/// 역할: 최근 RTOS 이벤트를 고정 크기 버퍼로 저장하는 trace 로그입니다.
/// </summary>
public sealed class RtosTraceLog
{
    private readonly object _syncRoot = new();
    private readonly Queue<RtosTraceEntry> _entries = new();

    /// <summary>
    /// 역할: 로그 버퍼를 생성합니다.
    /// </summary>
    /// <param name="capacity">최대 보관 개수입니다.</param>
    public RtosTraceLog(int capacity = 512)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Trace capacity must be greater than zero.");
        }

        Capacity = capacity;
    }

    /// <summary>
    /// 역할: 최대 보관 개수입니다.
    /// </summary>
    public int Capacity { get; }

    /// <summary>
    /// 역할: 로그 항목을 추가합니다.
    /// </summary>
    /// <param name="category">로그 분류입니다. 예: Scheduler, Task.</param>
    /// <param name="message">로그 메시지 본문입니다.</param>
    /// <param name="taskName">관련 태스크 이름(선택)입니다.</param>
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

    /// <summary>
    /// 역할: 현재 로그를 읽기 전용 목록으로 복사해 반환합니다.
    /// </summary>
    /// <returns>현재 시점 로그 스냅샷입니다.</returns>
    public IReadOnlyList<RtosTraceEntry> Snapshot()
    {
        lock (_syncRoot)
        {
            return _entries.ToArray();
        }
    }

    /// <summary>
    /// 역할: 버퍼에 저장된 로그를 모두 지웁니다.
    /// </summary>
    public void Clear()
    {
        lock (_syncRoot)
        {
            _entries.Clear();
        }
    }
}

/// <summary>
/// 역할: 단일 trace 로그 항목입니다.
/// </summary>
public sealed record RtosTraceEntry(
    DateTimeOffset CreatedAt,
    string Category,
    string Message,
    string? TaskName);
