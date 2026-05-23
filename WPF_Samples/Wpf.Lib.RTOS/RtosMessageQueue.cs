namespace Wpf.Lib.RTOS;

/// <summary>
/// 태스크 간 메시지를 주고받기 위한 고정 용량 FIFO 큐입니다.
/// </summary>
public sealed class RtosMessageQueue<T> : IDisposable
{
    private readonly object _syncRoot = new();
    private readonly Queue<T> _queue = new();
    private readonly SemaphoreSlim _items;
    private readonly SemaphoreSlim _spaces;
    private readonly CancellationTokenSource _shutdownCts = new();
    private bool _isDisposed;

    /// <summary>
    /// 메시지 큐를 생성합니다.
    /// </summary>
    /// <param name="capacity">최대 저장 개수입니다.</param>
    public RtosMessageQueue(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be greater than zero.");
        }

        Capacity = capacity;
        _items = new SemaphoreSlim(0, capacity);
        _spaces = new SemaphoreSlim(capacity, capacity);
    }

    /// <summary>
    /// 큐 최대 용량입니다.
    /// </summary>
    public int Capacity { get; }

    /// <summary>
    /// 현재 저장된 메시지 수입니다.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_syncRoot)
            {
                ThrowIfDisposed();
                return _queue.Count;
            }
        }
    }

    /// <summary>
    /// 메시지를 큐에 넣습니다.
    /// </summary>
    /// <param name="message">보낼 메시지입니다.</param>
    /// <param name="timeout">공간이 생길 때까지 대기할 시간입니다.</param>
    /// <param name="cancellationToken">취소 토큰입니다.</param>
    /// <returns>전송 성공 시 true, 타임아웃이면 false입니다.</returns>
    public async Task<bool> SendAsync(T message, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownCts.Token);

        bool acquiredSpace;
        try
        {
            acquiredSpace = await _spaces.WaitAsync(timeout, linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
        {
            throw new ObjectDisposedException(nameof(RtosMessageQueue<T>));
        }

        if (!acquiredSpace)
        {
            return false;
        }

        try
        {
            lock (_syncRoot)
            {
                if (_isDisposed)
                {
                    throw new ObjectDisposedException(nameof(RtosMessageQueue<T>));
                }

                _queue.Enqueue(message);
                _items.Release();
            }

            return true;
        }
        catch
        {
            ReleaseSpaceIfNotDisposed();
            throw;
        }
    }

    /// <summary>
    /// 메시지를 큐에서 하나 꺼냅니다.
    /// </summary>
    /// <param name="timeout">메시지를 기다릴 최대 시간입니다.</param>
    /// <param name="cancellationToken">취소 토큰입니다.</param>
    /// <returns>성공 여부와 메시지 값을 튜플로 반환합니다.</returns>
    public async Task<(bool Success, T? Message)> ReceiveAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownCts.Token);

        bool hasItem;
        try
        {
            hasItem = await _items.WaitAsync(timeout, linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
        {
            throw new ObjectDisposedException(nameof(RtosMessageQueue<T>));
        }

        if (!hasItem)
        {
            return (false, default);
        }

        T message;
        lock (_syncRoot)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(RtosMessageQueue<T>));
            }

            message = _queue.Dequeue();
            _spaces.Release();
        }

        return (true, message);
    }

    /// <summary>
    /// 큐와 내부 동기화 리소스를 해제합니다.
    /// </summary>
    public void Dispose()
    {
        bool shouldCancel;

        lock (_syncRoot)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _queue.Clear();
            shouldCancel = true;
        }

        if (shouldCancel)
        {
            _shutdownCts.Cancel();
        }
    }

    private void ThrowIfDisposed()
    {
        lock (_syncRoot)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
        }
    }

    private void ReleaseSpaceIfNotDisposed()
    {
        lock (_syncRoot)
        {
            if (_isDisposed)
            {
                return;
            }

            try
            {
                _spaces.Release();
            }
            catch (ObjectDisposedException)
            {
                // Dispose 경합 시에는 반환 토큰 복구를 생략한다.
            }
        }
    }
}
