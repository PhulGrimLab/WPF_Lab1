namespace Wpf.Lib.RTOS;

/// <summary>
/// 역할: 기준 시각과 tick 간격으로 현재 tick 값을 계산하는 도우미입니다.
/// </summary>
public sealed class RtosTickCounter
{
    /// <summary>
    /// 역할: tick 카운터를 생성합니다.
    /// </summary>
    /// <param name="tickInterval">tick 1회 시간 간격입니다.</param>
    public RtosTickCounter(TimeSpan tickInterval)
    {
        if (tickInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(tickInterval), "Tick interval must be greater than zero.");
        }

        TickInterval = tickInterval;
    }

    /// <summary>
    /// 역할: tick 간격입니다.
    /// </summary>
    public TimeSpan TickInterval { get; }

    /// <summary>
    /// 역할: 마지막 계산 결과 tick 값입니다.
    /// </summary>
    public long CurrentTick { get; private set; }

    /// <summary>
    /// 역할: tick 0 기준 시각입니다.
    /// </summary>
    public DateTimeOffset StartedAt { get; private set; } = DateTimeOffset.Now;

    /// <summary>
    /// 역할: 기준 시각과 tick 값을 초기화합니다.
    /// </summary>
    public void Reset(DateTimeOffset startedAt)
    {
        StartedAt = startedAt;
        CurrentTick = 0;
    }

    /// <summary>
    /// 역할: 지정 시각까지 경과한 tick을 계산합니다.
    /// </summary>
    /// <returns>계산된 현재 tick 값입니다.</returns>
    public long AdvanceTo(DateTimeOffset now)
    {
        var elapsed = now - StartedAt;
        if (elapsed <= TimeSpan.Zero)
        {
            CurrentTick = 0;
            return CurrentTick;
        }

        CurrentTick = elapsed.Ticks / TickInterval.Ticks;
        return CurrentTick;
    }

    /// <summary>
    /// 역할: 특정 tick 번호가 가리키는 실제 시각을 계산합니다.
    /// </summary>
    public DateTimeOffset GetTimeForTick(long tick)
    {
        if (tick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tick), "Tick must be zero or greater.");
        }

        try
        {
            return StartedAt + TimeSpan.FromTicks(checked(TickInterval.Ticks * tick));
        }
        catch (OverflowException ex)
        {
            throw new ArgumentOutOfRangeException(nameof(tick), tick, $"Tick is too large for the configured tick interval. {ex.Message}");
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new ArgumentOutOfRangeException(nameof(tick), tick, $"Tick produces a time outside the supported DateTimeOffset range. {ex.Message}");
        }
    }
}
