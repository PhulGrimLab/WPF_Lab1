namespace Wpf.Lib.RTOS;

/// <summary>
/// 비트 플래그 기반 이벤트 동기화를 제공하는 primitive입니다.
/// </summary>
public sealed class RtosEventFlags : IDisposable
{
    private readonly object _syncRoot = new();
    private readonly List<WaitRequest> _waiters = [];
    private readonly List<WaitRequest> _waitAllWaiters = [];
    private readonly Dictionary<uint, List<WaitRequest>> _waitAnyWaitersByBit = new();
    private uint _flags;
    private int _isDisposed;

    /// <summary>
    /// 현재 set된 플래그 비트 값입니다.
    /// </summary>
    public uint CurrentFlags
    {
        get
        {
            ThrowIfDisposed();

            lock (_syncRoot)
            {
                return _flags;
            }
        }
    }

    /// <summary>
    /// 지정 비트를 set하고 대기 중 요청을 평가합니다.
    /// </summary>
    /// <param name="flags">set할 비트 마스크입니다.</param>
    public void Set(uint flags)
    {
        ThrowIfDisposed();

        if (flags == 0)
        {
            return;
        }

        List<WaitRequest> completed = [];

        lock (_syncRoot)
        {
            _flags |= flags;
            CompleteMatchingWaiters(flags, completed);
        }

        foreach (var waiter in completed)
        {
            waiter.Complete();
        }
    }

    /// <summary>
    /// 지정 비트를 clear합니다.
    /// </summary>
    /// <param name="flags">clear할 비트 마스크입니다.</param>
    public void Clear(uint flags)
    {
        ThrowIfDisposed();

        lock (_syncRoot)
        {
            _flags &= ~flags;
        }
    }

    /// <summary>
    /// 요청 비트 중 하나라도 set될 때까지 대기합니다.
    /// </summary>
    /// <returns>매칭된 비트 마스크를 반환합니다.</returns>
    public Task<uint> WaitAnyAsync(uint flags, bool autoClear, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return WaitAsync(flags, waitAll: false, autoClear, timeout, cancellationToken);
    }

    /// <summary>
    /// 요청 비트가 모두 set될 때까지 대기합니다.
    /// </summary>
    /// <returns>매칭된 비트 마스크를 반환합니다.</returns>
    public Task<uint> WaitAllAsync(uint flags, bool autoClear, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return WaitAsync(flags, waitAll: true, autoClear, timeout, cancellationToken);
    }

    /// <summary>
    /// 대기 중 요청을 정리하고 리소스를 해제합니다.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        List<WaitRequest> waitersToCancel;

        lock (_syncRoot)
        {
            waitersToCancel = [.. _waiters];
            _waiters.Clear();
            _waitAllWaiters.Clear();
            _waitAnyWaitersByBit.Clear();
            _flags = 0;
        }

        foreach (var waiter in waitersToCancel)
        {
            waiter.CancelDueToDispose();
        }
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
            // Dispose와 경합하는 경우 lock 안에서 상태를 다시 확인해
            // dispose 이후 waiter가 목록에 남는 것을 방지한다.
            if (Volatile.Read(ref _isDisposed) != 0)
            {
                throw new ObjectDisposedException(nameof(RtosEventFlags));
            }

            var matched = GetMatchedFlags(flags, waitAll);
            if (matched != 0)
            {
                if (autoClear)
                {
                    _flags &= ~matched;
                }

                // 이미 조건이 충족된 경우 waiter를 만들지 않고 즉시 완료한다.
                return Task.FromResult(matched);
            }

            request = new WaitRequest(this, flags, waitAll, autoClear);
            _waiters.Add(request);

            if (waitAll)
            {
                _waitAllWaiters.Add(request);
            }
            else
            {
                IndexWaitAnyRequestUnsafe(request);
            }
        }

        try
        {
            request.StartTimeout(timeout, cancellationToken);
        }
        catch
        {
            RemoveWaiter(request);
            throw;
        }

        return request.Task;
    }

    private void CompleteMatchingWaiters(uint setFlags, List<WaitRequest> completed)
    {
        CompleteMatchingWaitAllWaiters(completed);
        CompleteMatchingWaitAnyWaiters(setFlags, completed);
    }

    private void CompleteMatchingWaitAllWaiters(List<WaitRequest> completed)
    {
        // waiter를 뒤에서부터 순회하면 RemoveAt 시 인덱스 보정이 단순해진다.
        for (var index = _waitAllWaiters.Count - 1; index >= 0; index--)
        {
            var waiter = _waitAllWaiters[index];
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
            _waiters.Remove(waiter);
            _waitAllWaiters.RemoveAt(index);
            completed.Add(waiter);
        }
    }

    private void CompleteMatchingWaitAnyWaiters(uint setFlags, List<WaitRequest> completed)
    {
        if (setFlags == 0 || _waitAnyWaitersByBit.Count == 0)
        {
            return;
        }

        HashSet<WaitRequest>? visited = null;

        foreach (var bit in EnumerateBits(setFlags))
        {
            if (!_waitAnyWaitersByBit.TryGetValue(bit, out var bucket) || bucket.Count == 0)
            {
                continue;
            }

            for (var index = bucket.Count - 1; index >= 0; index--)
            {
                var waiter = bucket[index];
                visited ??= new HashSet<WaitRequest>();
                if (!visited.Add(waiter))
                {
                    continue;
                }

                var matched = GetMatchedFlags(waiter.Flags, waitAll: false);
                if (matched == 0)
                {
                    continue;
                }

                if (waiter.AutoClear)
                {
                    _flags &= ~matched;
                }

                waiter.MatchedFlags = matched;
                DetachWaiterUnsafe(waiter);
                completed.Add(waiter);
            }
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
            DetachWaiterUnsafe(waiter);
        }
    }

    private void DetachWaiterUnsafe(WaitRequest waiter)
    {
        _waiters.Remove(waiter);

        if (waiter.WaitAll)
        {
            _waitAllWaiters.Remove(waiter);
            return;
        }

        foreach (var bit in EnumerateBits(waiter.Flags))
        {
            if (!_waitAnyWaitersByBit.TryGetValue(bit, out var bucket))
            {
                continue;
            }

            bucket.Remove(waiter);
            if (bucket.Count == 0)
            {
                _waitAnyWaitersByBit.Remove(bit);
            }
        }
    }

    private void IndexWaitAnyRequestUnsafe(WaitRequest request)
    {
        foreach (var bit in EnumerateBits(request.Flags))
        {
            if (!_waitAnyWaitersByBit.TryGetValue(bit, out var bucket))
            {
                bucket = [];
                _waitAnyWaitersByBit.Add(bit, bucket);
            }

            bucket.Add(request);
        }
    }

    private static IEnumerable<uint> EnumerateBits(uint flags)
    {
        while (flags != 0)
        {
            var bit = flags & (~flags + 1);
            yield return bit;
            flags &= ~bit;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);
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

            try
            {
                if (timeout != Timeout.InfiniteTimeSpan)
                {
                    // timeout용 취소 토큰과 외부 cancellation 토큰을 분리해
                    // timeout/cancellation/dispose 원인을 명확히 구분한다.
                    _timeoutCts = new CancellationTokenSource();
                    _timeoutRegistration = _timeoutCts.Token.Register(static state =>
                    {
                        ((WaitRequest)state!).CancelDueToTimeout();
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
                        ((WaitRequest)state!).CancelDueToCancellation();
                    }, this);
                    if (Volatile.Read(ref _isCompleted) != 0)
                    {
                        _cancellationRegistration.Dispose();
                    }
                }
            }
            catch
            {
                Cleanup();
                throw;
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

        private void CancelDueToCancellation()
        {
            if (Interlocked.Exchange(ref _isCompleted, 1) != 0)
            {
                return;
            }

            _owner.RemoveWaiter(this);
            Cleanup();
            _completion.TrySetCanceled();
        }

        private void CancelDueToTimeout()
        {
            if (Interlocked.Exchange(ref _isCompleted, 1) != 0)
            {
                return;
            }

            _owner.RemoveWaiter(this);
            Cleanup();
            _completion.TrySetException(new TimeoutException("Event flags wait timed out."));
        }

        public void CancelDueToDispose()
        {
            if (Interlocked.Exchange(ref _isCompleted, 1) != 0)
            {
                return;
            }

            Cleanup();
            _completion.TrySetException(new ObjectDisposedException(nameof(RtosEventFlags)));
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
