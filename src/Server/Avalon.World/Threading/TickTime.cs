namespace Avalon.World.Threading;

/// <summary>
/// "Now" for one tick (#977): the container's clock read once, both ways, at the start of the tick, and handed to
/// what the tick asks the time of for every connection (the lease check, and through it each packet's authorization).
/// What that work decides by the time is decided once per tick, and never by a clock read per connection or per
/// packet. Work that measures a duration (a handler's run, a tick phase) still reads its own clock.
/// </summary>
/// <param name="Clock">The clock both were read from.</param>
/// <param name="UtcNow">The wall clock: <see cref="TimeProvider.GetUtcNow" />.</param>
/// <param name="Timestamp">The monotonic clock: <see cref="TimeProvider.GetTimestamp" />, in the clock's frequency.</param>
public readonly record struct TickTime(TimeProvider Clock, DateTime UtcNow, long Timestamp)
{
    public static TickTime Read(TimeProvider clock) => new(clock, clock.GetUtcNow().UtcDateTime, clock.GetTimestamp());

    /// <summary>
    /// This time, if it was read from <paramref name="clock" />; else <paramref name="clock" /> read now. A timestamp
    /// means nothing on another clock, and in production every clock is the container's, so this never reads.
    /// </summary>
    public TickTime On(TimeProvider clock) => ReferenceEquals(clock, Clock) ? this : Read(clock);
}
