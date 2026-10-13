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
    private readonly Dictionary<int, (ICreature Creature, long RemoveAtMs)> _waiting = new();

    /// <summary>This tick's expired corpses, reused so a tick allocates nothing (#640).</summary>
    private readonly List<int> _expired = new();

    // Whole milliseconds of every Update so far: what a timer per corpse counted for itself, counted once.
    private long _elapsedMs;
    private int _scheduled;

    public void ScheduleRemoval(ICreature creature) =>
        _waiting.Add(_scheduled++, (creature, _elapsedMs + (long)creature.Metadata.BodyRemoveTimer.TotalMilliseconds));

    public void Update(TimeSpan deltaTime)
    {
        _expired.Clear();
        _elapsedMs += (long)deltaTime.TotalMilliseconds;

        foreach ((int scheduled, (ICreature creature, long removeAtMs)) in _waiting)
        {
            if (_elapsedMs < removeAtMs)
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
}
