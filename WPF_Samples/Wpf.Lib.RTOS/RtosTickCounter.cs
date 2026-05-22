namespace Wpf.Lib.RTOS;

public sealed class RtosTickCounter
{
    public RtosTickCounter(TimeSpan tickInterval)
    {
        if (tickInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(tickInterval), "Tick interval must be greater than zero.");
        }

        TickInterval = tickInterval;
    }

    public TimeSpan TickInterval { get; }
    public long CurrentTick { get; private set; }

    public DateTimeOffset StartedAt { get; private set; } = DateTimeOffset.Now;

    public void Reset(DateTimeOffset startedAt)
    {
        StartedAt = startedAt;
        CurrentTick = 0;
    }

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
