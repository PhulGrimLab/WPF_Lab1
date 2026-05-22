namespace Wpf.Lib.RTOS;

public sealed class RtosMessageQueue<T> : IDisposable
{
    private readonly object _syncRoot = new();
    private readonly Queue<T> _queue = new();
    private readonly SemaphoreSlim _items;
    private readonly SemaphoreSlim _spaces;
    private bool _isDisposed;

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

    public int Capacity { get; }

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

    public async Task<bool> SendAsync(T message, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!await _spaces.WaitAsync(timeout, cancellationToken).ConfigureAwait(false))
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

    public async Task<(bool Success, T? Message)> ReceiveAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!await _items.WaitAsync(timeout, cancellationToken).ConfigureAwait(false))
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

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _queue.Clear();
            _items.Dispose();
            _spaces.Dispose();
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

            _spaces.Release();
        }
    }
}
