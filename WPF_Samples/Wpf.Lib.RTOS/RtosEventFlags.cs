namespace Wpf.Lib.RTOS;

public sealed class RtosEventFlags
{
    private readonly object _syncRoot = new();
    private readonly List<WaitRequest> _waiters = [];
    private uint _flags;

    public uint CurrentFlags
    {
        get
        {
            lock (_syncRoot)
            {
                return _flags;
            }
        }
    }

    public void Set(uint flags)
    {
        if (flags == 0)
        {
            return;
        }

        List<WaitRequest> completed = [];

        lock (_syncRoot)
        {
            _flags |= flags;
            CompleteMatchingWaiters(completed);
        }

        foreach (var waiter in completed)
        {
            waiter.Complete();
        }
    }

    public void Clear(uint flags)
    {
        lock (_syncRoot)
        {
            _flags &= ~flags;
        }
    }

    public Task<uint> WaitAnyAsync(uint flags, bool autoClear, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        return WaitAsync(flags, waitAll: false, autoClear, timeout, cancellationToken);
    }

    public Task<uint> WaitAllAsync(uint flags, bool autoClear, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        return WaitAsync(flags, waitAll: true, autoClear, timeout, cancellationToken);
    }

    private Task<uint> WaitAsync(uint flags, bool waitAll, bool autoClear, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (flags == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(flags), "Flags must not be zero.");
        }

        if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be non-negative or Timeout.InfiniteTimeSpan.");
        }

        WaitRequest request;

        lock (_syncRoot)
        {
            var matched = GetMatchedFlags(flags, waitAll);
            if (matched != 0)
            {
                if (autoClear)
                {
                    _flags &= ~matched;
                }

                return Task.FromResult(matched);
            }

            request = new WaitRequest(this, flags, waitAll, autoClear);
            _waiters.Add(request);
        }

        request.StartTimeout(timeout, cancellationToken);
        return request.Task;
    }

    private void CompleteMatchingWaiters(List<WaitRequest> completed)
    {
        for (var index = _waiters.Count - 1; index >= 0; index--)
        {
            var waiter = _waiters[index];
            var matched = GetMatchedFlags(waiter.Flags, waiter.WaitAll);
            if (matched == 0)
            {
                continue;
            }

            if (waiter.AutoClear)
            {
                _flags &= ~matched;
            }

            waiter.MatchedFlags = matched;
            _waiters.RemoveAt(index);
            completed.Add(waiter);
        }
    }

    private uint GetMatchedFlags(uint flags, bool waitAll)
    {
        var matched = _flags & flags;
        return waitAll && matched != flags
            ? 0
            : matched;
    }

    private void RemoveWaiter(WaitRequest waiter)
    {
        lock (_syncRoot)
        {
            _waiters.Remove(waiter);
        }
    }

    private sealed class WaitRequest
    {
        private readonly RtosEventFlags _owner;
        private readonly TaskCompletionSource<uint> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private CancellationTokenSource? _timeoutCts;
        private CancellationTokenRegistration _timeoutRegistration;
        private CancellationTokenRegistration _cancellationRegistration;
        private int _isCompleted;

        public WaitRequest(RtosEventFlags owner, uint flags, bool waitAll, bool autoClear)
        {
            _owner = owner;
            Flags = flags;
            WaitAll = waitAll;
            AutoClear = autoClear;
        }

        public uint Flags { get; }
        public bool WaitAll { get; }
        public bool AutoClear { get; }
        public uint MatchedFlags { get; set; }
        public Task<uint> Task => _completion.Task;

        public void StartTimeout(TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref _isCompleted) != 0)
            {
                return;
            }

            if (timeout == Timeout.InfiniteTimeSpan && !cancellationToken.CanBeCanceled)
            {
                return;
            }

            if (timeout != Timeout.InfiniteTimeSpan)
            {
                _timeoutCts = new CancellationTokenSource();
                _timeoutRegistration = _timeoutCts.Token.Register(static state =>
                {
                    ((WaitRequest)state!).Cancel();
                }, this);
                if (Volatile.Read(ref _isCompleted) != 0)
                {
                    _timeoutRegistration.Dispose();
                    _timeoutCts.Dispose();
                    _timeoutCts = null;
                    return;
                }

                _timeoutCts.CancelAfter(timeout);
            }

            if (cancellationToken.CanBeCanceled)
            {
                _cancellationRegistration = cancellationToken.Register(static state =>
                {
                    ((WaitRequest)state!).Cancel();
                }, this);
                if (Volatile.Read(ref _isCompleted) != 0)
                {
                    _cancellationRegistration.Dispose();
                }
            }
        }

        public void Complete()
        {
            if (Interlocked.Exchange(ref _isCompleted, 1) != 0)
            {
                return;
            }

            Cleanup();
            _completion.TrySetResult(MatchedFlags);
        }

        private void Cancel()
        {
            if (Interlocked.Exchange(ref _isCompleted, 1) != 0)
            {
                return;
            }

            _owner.RemoveWaiter(this);
            Cleanup();
            _completion.TrySetCanceled();
        }

        private void Cleanup()
        {
            _timeoutRegistration.Dispose();
            _cancellationRegistration.Dispose();
            _timeoutCts?.Dispose();
            _timeoutCts = null;
        }
    }
}
