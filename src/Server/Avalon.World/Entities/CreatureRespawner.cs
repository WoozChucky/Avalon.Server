using Avalon.Common.Utils;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Maps;

namespace Avalon.World.Entities;

public interface ICreatureRespawner
{
    void ScheduleRespawn(ICreature creature);
    void Update(TimeSpan deltaTime);
}

public class CreatureRespawner(IChunk chunk) : ICreatureRespawner
{
    private readonly IDictionary<IntervalTimer, ICreature> _removeTimers = new Dictionary<IntervalTimer, ICreature>();
    private readonly IDictionary<IntervalTimer, ICreature> _respawnTimers = new Dictionary<IntervalTimer, ICreature>();

    public void ScheduleRespawn(ICreature creature)
    {
        IntervalTimer respawnTimer = new();
        respawnTimer.SetInterval((long)TimeSpan.FromMinutes(3)
            .TotalMilliseconds); //TODO: Respawn timer from creature template metadata
        respawnTimer.SetCurrent(0);
        _respawnTimers.Add(respawnTimer, creature);

        IntervalTimer removeTimer = new();
        removeTimer.SetInterval((long)TimeSpan.FromMinutes(2)
            .TotalMilliseconds); //TODO: Remove timer from creature template metadata
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

            chunk.RemoveCreature(creature);
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

            chunk.RespawnCreature(creature);
            timersToRespawn.Add(timer);
        }

        foreach (IntervalTimer timer in timersToRespawn)
        {
            _respawnTimers.Remove(timer);
        }
    }
}
