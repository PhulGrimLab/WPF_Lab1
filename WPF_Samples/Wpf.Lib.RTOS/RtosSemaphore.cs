namespace Wpf.Lib.RTOS;

/// <summary>
/// 제한된 개수의 자원을 동시에 접근할 수 있게 하는 세마포어 래퍼입니다.
/// </summary>
public sealed class RtosSemaphore : IDisposable
{
    private readonly SemaphoreSlim _semaphore;
    private int _isDisposed;

    /// <summary>
    /// 세마포어를 생성합니다.
    /// </summary>
    /// <param name="initialCount">초기 가용 토큰 수입니다.</param>
    /// <param name="maxCount">최대 토큰 수입니다.</param>
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

    /// <summary>
    /// 현재 가용 토큰 수를 반환합니다.
    /// </summary>
    public int CurrentCount
    {
        get
        {
            ThrowIfDisposed();
            return _semaphore.CurrentCount;
        }
    }

    /// <summary>
    /// 토큰 획득을 시도합니다.
    /// </summary>
    /// <param name="timeout">대기 시간입니다.</param>
    /// <param name="cancellationToken">취소 토큰입니다.</param>
    /// <returns>획득 성공 시 true, 타임아웃이면 false입니다.</returns>
    public async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return await _semaphore.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 토큰을 1개 반환합니다.
    /// </summary>
    public void Release()
    {
        ThrowIfDisposed();
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

        _semaphore.Dispose();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);
    }
}
