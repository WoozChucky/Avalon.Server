namespace Avalon.World.Instances;

/// <summary>
/// When the instances broadcast their state (#975): each every <see cref="Interval" />, in one of <see cref="Slots" />
/// slots a tick apart, so the world's broadcasts are spread over the ticks of an interval instead of all made on one.
/// One per world, owned by its <see cref="InstanceTicker" />, which advances it by every tick's delta; the
/// <see cref="InstanceRegistry" /> places each instance it publishes in the slot with the fewest live instances
/// (<see cref="Join" />) and frees the slot when it lets the instance go (<see cref="Leave" />). An instance keeps its own
/// clock from then on (<c>MapInstance</c>), so nothing here is read while the instances tick. Tick thread only.
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

    // How many live instances each slot holds.
    private readonly int[] _live = new int[Slots];

    /// <summary>Moves the schedule on by one tick's delta; the ticker calls it once a tick.</summary>
    public void Advance(TimeSpan deltaTime)
    {
        long clock = _clock + deltaTime.Ticks;
        _clock = clock >= IntervalTicks ? clock % IntervalTicks : clock;
    }

    /// <summary>
    /// Places an instance published now: in the slot holding the fewest live instances (the lowest such slot on a tie,
    /// so instances published together take consecutive slots), counted until <see cref="Leave" />. Returns the slot and
    /// the broadcast clock the instance starts from: the world's, moved on by that slot's share of the interval.
    /// </summary>
    public TimeSpan Join(out int slot)
    {
        slot = 0;
        for (int i = 1; i < Slots; i++)
        {
            if (_live[i] < _live[slot])
                slot = i;
        }

        _live[slot]++;
        return TimeSpan.FromTicks((_clock + slot * IntervalTicks / Slots) % IntervalTicks);
    }

    /// <summary>An instance placed by <see cref="Join" /> has gone: its slot holds one fewer.</summary>
    public void Leave(int slot)
    {
        if (_live[slot] > 0)
            _live[slot]--;
    }
}
