namespace Avalon.World.Instances;

/// <summary>
/// When the instances broadcast their state (#975): each every <see cref="Interval" />, in one of <see cref="Slots" />
/// slots a tick apart, so the world's broadcasts are spread over the ticks of an interval instead of all made on one.
/// One per world, owned by its <see cref="InstanceTicker" />, which advances it by every tick's delta; the
/// <see cref="InstanceRegistry" /> hands each instance it publishes the next slot, round-robin
/// (<see cref="NextPhase" />). An instance keeps its own clock from then on (<c>MapInstance</c>), so nothing here is read
/// while the instances tick. Tick thread only.
/// </summary>
/// <remarks>
/// Every live instance is ticked by the same deltas, empty ones included, so the phase an instance is given at
/// publication stays its place on the world's schedule for as long as it lives, whatever the tick lengths, stalls or
/// empty stretches in between; no other instance's coming or going moves it.
/// </remarks>
public sealed class BroadcastSchedule
{
    /// <summary>The time between two state broadcasts of one instance: ten a second.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromTicks(IntervalTicks);

    /// <summary>The slots of an interval: its ticks at the World server's 60 Hz.</summary>
    public const int Slots = 6;

    internal const long IntervalTicks = 100 * TimeSpan.TicksPerMillisecond;

    // The time the instances have been ticked by, modulo the interval.
    private long _clock;
    private int _nextSlot;

    /// <summary>Moves the schedule on by one tick's delta; the ticker calls it once a tick.</summary>
    public void Advance(TimeSpan deltaTime)
    {
        long clock = _clock + deltaTime.Ticks;
        _clock = clock >= IntervalTicks ? clock % IntervalTicks : clock;
    }

    /// <summary>
    /// The broadcast clock an instance published now starts from: the world's, moved on by the next slot's share of the
    /// interval. Instances published together get consecutive slots; the slot after the last is the first again.
    /// </summary>
    public TimeSpan NextPhase()
    {
        int slot = _nextSlot;
        _nextSlot = (slot + 1) % Slots;
        return TimeSpan.FromTicks((_clock + slot * IntervalTicks / Slots) % IntervalTicks);
    }
}
