using Avalon.Common.Utils;
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
    private readonly IDictionary<IntervalTimer, ICreature> _removeTimers = new Dictionary<IntervalTimer, ICreature>();

    public void ScheduleRemoval(ICreature creature)
    {
        IntervalTimer removeTimer = new();
        removeTimer.SetInterval((long)creature.Metadata.BodyRemoveTimer.TotalMilliseconds);
        removeTimer.SetCurrent(0);
        _removeTimers.Add(removeTimer, creature);
    }

    public void Update(TimeSpan deltaTime)
    {
        List<IntervalTimer> expired = new();

        foreach ((IntervalTimer timer, ICreature creature) in _removeTimers)
        {
            if (timer.GetCurrent() >= 0)
            {
                timer.Update((long)deltaTime.TotalMilliseconds);
            }

            if (!timer.Passed())
            {
                continue;
            }

            context.RemoveCreature(creature);
            expired.Add(timer);
        }

        foreach (IntervalTimer timer in expired)
        {
            _removeTimers.Remove(timer);
        }
    }
}
