using Avalon.Common.Utils;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;

namespace Avalon.World.Entities;

public interface ICreatureRespawner
{
    void ScheduleRespawn(ICreature creature);
    void Update(TimeSpan deltaTime);
}

public class CreatureRespawner(ISimulationContext context) : ICreatureRespawner
{
    private readonly IDictionary<IntervalTimer, ICreature> _removeTimers = new Dictionary<IntervalTimer, ICreature>();
    private readonly IDictionary<IntervalTimer, ICreature> _respawnTimers = new Dictionary<IntervalTimer, ICreature>();

    public void ScheduleRespawn(ICreature creature)
    {
        IntervalTimer respawnTimer = new();
        respawnTimer.SetInterval((long)creature.Metadata.RespawnTimer.TotalMilliseconds);
        respawnTimer.SetCurrent(0);
        _respawnTimers.Add(respawnTimer, creature);

        IntervalTimer removeTimer = new();
        removeTimer.SetInterval((long)creature.Metadata.BodyRemoveTimer.TotalMilliseconds);
        removeTimer.SetCurrent(0);
        _removeTimers.Add(removeTimer, creature);
    }

    public void Update(TimeSpan deltaTime)
    {
        List<IntervalTimer> timersToRemove = new();
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
            timersToRemove.Add(timer);
        }

        foreach (IntervalTimer timer in timersToRemove)
        {
            _removeTimers.Remove(timer);
        }

        List<IntervalTimer> timersToRespawn = new();

        foreach ((IntervalTimer timer, ICreature creature) in _respawnTimers)
        {
            if (timer.GetCurrent() >= 0)
            {
                timer.Update((long)deltaTime.TotalMilliseconds);
            }

            if (!timer.Passed())
            {
                continue;
            }

            context.RespawnCreature(creature);
            timersToRespawn.Add(timer);
        }

        foreach (IntervalTimer timer in timersToRespawn)
        {
            _respawnTimers.Remove(timer);
        }
    }
}
