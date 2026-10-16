using System.Diagnostics.Metrics;

namespace Avalon.Infrastructure.WorldPlaces;

/// <summary>The queue's outcomes and wait (#967), on identity's meter, tagged by world.</summary>
public sealed class WorldPlaceMetrics
{
    private readonly Counter<long> _outcomes;
    private readonly Histogram<double> _wait;

    public WorldPlaceMetrics(Meter meter)
    {
        _outcomes = meter.CreateCounter<long>("avalon.world.queue.outcomes", "{accounts}",
            "How waits in a world's line ended: entered, cancelled, timed_out, turn_expired, refused_at_cap");
        _wait = meter.CreateHistogram<double>("avalon.world.queue.wait", "s", "Time in the line of those who entered");
    }

    /// <summary>What a script's sweep removed: turns not used in time, and waiters that stopped checking in.</summary>
    public void Record(ushort worldId, PlaceSweep sweep)
    {
        if (sweep.TurnsExpired > 0) Add(worldId, "turn_expired", sweep.TurnsExpired);
        if (sweep.WaitersTimedOut > 0) Add(worldId, "timed_out", sweep.WaitersTimedOut);
    }

    /// <summary>A waiter's join after its turn issued a ticket, <paramref name="waited"/> after it joined the line.</summary>
    public void Entered(ushort worldId, TimeSpan waited)
    {
        Add(worldId, "entered", 1);
        _wait.Record(waited.TotalSeconds, new KeyValuePair<string, object?>("world", (int)worldId));
    }

    /// <summary>A waiter left the line, or gave back a turn it had not used.</summary>
    public void Cancelled(ushort worldId) => Add(worldId, "cancelled", 1);

    /// <summary>A join refused <c>WORLD_FULL</c>: the world full and its line at its cap.</summary>
    public void RefusedAtCap(ushort worldId) => Add(worldId, "refused_at_cap", 1);

    private void Add(ushort worldId, string reason, int count) => _outcomes.Add(count,
        new KeyValuePair<string, object?>("world", (int)worldId), new KeyValuePair<string, object?>("reason", reason));
}
