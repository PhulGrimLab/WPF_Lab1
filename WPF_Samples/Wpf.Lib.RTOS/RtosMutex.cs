namespace Wpf.Lib.RTOS;

public sealed class RtosMutex : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly object _syncRoot = new();
    private readonly Dictionary<long, Enum_TaskPriority> _waitingPriorities = [];
    private long _nextWaiterId;
    private string? _owner;
    private Enum_TaskPriority _ownerBasePriority;
    private int _isDisposed;

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

    public async Task<bool> WaitAsync(string owner, Enum_TaskPriority priority, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(owner))
        {
            throw new ArgumentException("Owner must not be empty.", nameof(owner));
        }

        var waiterId = 0L;

        lock (_syncRoot)
        {
            if (_owner is not null)
            {
                waiterId = ++_nextWaiterId;
                _waitingPriorities[waiterId] = priority;
            }
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
                if (waiterId != 0)
                {
                    _waitingPriorities.Remove(waiterId);
                }
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
