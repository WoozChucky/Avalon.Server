using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;

namespace Avalon.World.Entities;

/// <summary>
/// Takes a dead creature's corpse out of its instance once the creature's template says enough time
/// has passed.
/// </summary>
/// <remarks>
/// This replaces the time-based respawn scheduler that used to live here. Creatures no longer come
/// back on a clock — the design moved from timed respawns to revival only through a deliberate
/// mechanic — so the respawn half was removed rather than left wired to nothing. Corpse removal is
/// still needed, and for a plainer reason: without it a dead creature stays in the instance's entity
/// dictionary forever, ticked and broadcast, and on a busy map the corpses eventually carpet the floor.
/// </remarks>
public interface ICorpseRemover
{
    /// <summary>Starts the removal countdown for a creature that has just died.</summary>
    void ScheduleRemoval(ICreature creature);

    void Update(TimeSpan deltaTime);
}

public class CreatureCorpseRemover(ISimulationContext context) : ICorpseRemover
{
    // Each waiting corpse with the moment, on this remover's own clock, its body goes. Keyed by the order the corpses
    // were scheduled in, a number: a timer object per corpse as the key was 32 B for every kill (#883). The concrete
    // type, so the tick walks it with its struct enumerator (#640); and still a dictionary added to and removed from
    // exactly as before, so corpses that go on the same tick go in the order they always went.
    private readonly Dictionary<int, (ICreature Creature, long RemoveAtTicks)> _waiting = new();

    /// <summary>This tick's expired corpses, reused so a tick allocates nothing (#640).</summary>
    private readonly List<int> _expired = new();

    // The time of every Update so far, in TimeSpan ticks (100 ns), which is all a TimeSpan holds: nothing of a tick is
    // dropped, so a corpse waits what its template says. Counted in whole milliseconds, a 60 Hz tick of 16.67 ms
    // counted as 16, and a 10 s wait took 625 ticks, 10.4 s.
    private long _elapsedTicks;
    private int _scheduled;

    public void ScheduleRemoval(ICreature creature) =>
        _waiting.Add(_scheduled++, (creature, Sum(_elapsedTicks, creature.Metadata.BodyRemoveTimer.Ticks)));

    public void Update(TimeSpan deltaTime)
    {
        _expired.Clear();
        _elapsedTicks = Sum(_elapsedTicks, deltaTime.Ticks);

        foreach ((int scheduled, (ICreature creature, long removeAtTicks)) in _waiting)
        {
            if (_elapsedTicks < removeAtTicks)
            {
                continue;
            }

            context.RemoveCreature(creature);
            _expired.Add(scheduled);
        }

        foreach (int scheduled in _expired)
        {
            _waiting.Remove(scheduled);
        }
    }

    // The sum, or the end of the range it would have passed. TimeSpan.MaxValue is long.MaxValue ticks, so one such
    // delta or wait would wrap a plain sum: a clock gone negative would leave every corpse where it lies for good.
    private static long Sum(long ticks, long more)
    {
        long sum = unchecked(ticks + more);
        return ((ticks ^ sum) & (more ^ sum)) < 0 ? (more < 0 ? long.MinValue : long.MaxValue) : sum;
    }
}
