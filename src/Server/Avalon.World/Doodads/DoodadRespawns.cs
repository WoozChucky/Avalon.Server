namespace Avalon.World.Doodads;

/// <summary>
/// The spent doodads of one instance waiting to be ready again, as a min-heap on their respawn time: MapInstance's tick
/// peeks at the soonest once and stops at the first one not due, so the cost is per respawn, never per doodad. Times are
/// absolute, on the container's clock, so an instance nobody was in (which does not tick) catches up on its next
/// occupied tick. Tick thread only.
/// </summary>
public sealed class DoodadRespawns
{
    private readonly PriorityQueue<Doodad, DateTime> _due = new();

    /// <summary>How many respawns are waiting, superseded ones included.</summary>
    public int Count => _due.Count;

    /// <summary>
    /// Makes <paramref name="doodad" /> ready again at <paramref name="at" />. Scheduling it again before then moves it:
    /// the time in <see cref="Doodad.RespawnAt" /> is the one that counts, and an entry for another time is dropped when
    /// it comes up.
    /// </summary>
    public void Schedule(Doodad doodad, DateTime at)
    {
        doodad.RespawnAt = at;
        _due.Enqueue(doodad, at);
    }

    /// <summary>Respawns every doodad due by <paramref name="now" />, soonest first, each through the use service.</summary>
    public void Update(IDoodadHost host, DateTime now, DoodadUseService service)
    {
        while (_due.TryPeek(out Doodad? doodad, out DateTime at) && at <= now)
        {
            _due.Dequeue();
            if (doodad.RespawnAt != at)
                continue;

            doodad.RespawnAt = null;
            service.Respawned(host, doodad);
        }
    }
}
