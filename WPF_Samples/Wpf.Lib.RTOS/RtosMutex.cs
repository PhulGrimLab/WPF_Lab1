namespace Wpf.Lib.RTOS;

/// <summary>
/// 단일 소유자 잠금을 제공하는 mutex 래퍼입니다.
/// </summary>
public sealed class RtosMutex : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly object _syncRoot = new();
    private readonly Dictionary<long, Enum_TaskPriority> _waitingPriorities = [];
    private long _nextWaiterId;
    private string? _owner;
    private Enum_TaskPriority _ownerBasePriority;
    private int _isDisposed;

    /// <summary>
    /// 현재 mutex를 소유한 owner 이름입니다.
    /// </summary>
    public string? Owner
    {
        get
        {
            ThrowIfDisposed();

            lock (_syncRoot)
            {
                return _owner;
            }
        }
    }

    /// <summary>
    /// 대기 중 태스크 우선순위를 반영한 현재 owner의 유효 우선순위입니다.
    /// </summary>
    public Enum_TaskPriority? EffectiveOwnerPriority
    {
        get
        {
            ThrowIfDisposed();

            lock (_syncRoot)
            {
                if (_owner is null)
                {
                    return null;
                }

                return _waitingPriorities.Count == 0
                    ? _ownerBasePriority
                    : Max(_ownerBasePriority, _waitingPriorities.Values.Max());
            }
        }
    }

    /// <summary>
    /// owner 이름으로 mutex 획득을 시도합니다.
    /// </summary>
    /// <param name="owner">잠금 소유자를 구분하는 문자열입니다.</param>
    /// <param name="priority">요청 태스크 우선순위입니다.</param>
    /// <param name="timeout">대기 시간입니다.</param>
    /// <param name="cancellationToken">취소 토큰입니다.</param>
    /// <returns>획득 성공 시 true, 타임아웃이면 false입니다.</returns>
    public async Task<bool> WaitAsync(string owner, Enum_TaskPriority priority, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(owner))
        {
            throw new ArgumentException("Owner must not be empty.", nameof(owner));
        }

        long waiterId;

        lock (_syncRoot)
        {
            if (string.Equals(_owner, owner, StringComparison.Ordinal))
            {
                throw new SynchronizationLockException("Mutex is not reentrant for the same owner.");
            }

            // owner 상태 변화와 경합해도 대기자 우선순위가 누락되지 않도록
            // Wait 진입 시점에 waiter를 먼저 등록한다.
            waiterId = ++_nextWaiterId;
            _waitingPriorities[waiterId] = priority;
        }

        var acquired = false;

        try
        {
            acquired = await _semaphore.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_syncRoot)
            {
                _waitingPriorities.Remove(waiterId);
            }
        }

        if (!acquired)
        {
            return false;
        }

        lock (_syncRoot)
        {
            _owner = owner;
            _ownerBasePriority = priority;
        }

        return true;
    }

    /// <summary>
    /// 현재 owner가 mutex를 해제합니다.
    /// </summary>
    /// <param name="owner">해제 요청 owner 문자열입니다.</param>
    public void Release(string owner)
    {
        ThrowIfDisposed();

        lock (_syncRoot)
        {
            if (_owner is null)
            {
                throw new SynchronizationLockException("Mutex is not owned.");
            }

            if (!string.Equals(_owner, owner, StringComparison.Ordinal))
            {
                throw new SynchronizationLockException("Mutex can only be released by its owner.");
            }

            _owner = null;
        }

        _semaphore.Release();
    }

    /// <summary>
    /// 내부 리소스를 해제합니다.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        lock (_syncRoot)
        {
            _owner = null;
            _waitingPriorities.Clear();
        }

        _semaphore.Dispose();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);
    }

    private static Enum_TaskPriority Max(Enum_TaskPriority left, Enum_TaskPriority right)
    {
        return left >= right ? left : right;
    }
}
