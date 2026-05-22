namespace Wpf.Lib.RTOS;

public sealed class RtosSemaphore : IDisposable
{
    private readonly SemaphoreSlim _semaphore;
    private int _isDisposed;

    public RtosSemaphore(int initialCount, int maxCount)
    {
        if (maxCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCount), "Max count must be greater than zero.");
        }

        if (initialCount < 0 || initialCount > maxCount)
        {
            throw new ArgumentOutOfRangeException(nameof(initialCount), "Initial count must be between zero and max count.");
        }

        _semaphore = new SemaphoreSlim(initialCount, maxCount);
    }

    public int CurrentCount
    {
        get
        {
            ThrowIfDisposed();
            return _semaphore.CurrentCount;
        }
    }

    public async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return await _semaphore.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
    }

    public void Release()
    {
        ThrowIfDisposed();
        _semaphore.Release();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        _semaphore.Dispose();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);
    }
}
