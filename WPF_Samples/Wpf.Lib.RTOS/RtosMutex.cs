namespace Wpf.Lib.RTOS;

public sealed class RtosMutex
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly object _syncRoot = new();
    private readonly List<Enum_TaskPriority> _waitingPriorities = [];
    private string? _owner;
    private Enum_TaskPriority _ownerBasePriority;

    public string? Owner
    {
        get
        {
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
            lock (_syncRoot)
            {
                if (_owner is null)
                {
                    return null;
                }

                return _waitingPriorities.Count == 0
                    ? _ownerBasePriority
                    : Max(_ownerBasePriority, _waitingPriorities.Max());
            }
        }
    }

    public async Task<bool> WaitAsync(string owner, Enum_TaskPriority priority, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(owner))
        {
            throw new ArgumentException("Owner must not be empty.", nameof(owner));
        }

        lock (_syncRoot)
        {
            if (_owner is not null)
            {
                _waitingPriorities.Add(priority);
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
                _waitingPriorities.Remove(priority);
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

    private static Enum_TaskPriority Max(Enum_TaskPriority left, Enum_TaskPriority right)
    {
        return left >= right ? left : right;
    }
}
