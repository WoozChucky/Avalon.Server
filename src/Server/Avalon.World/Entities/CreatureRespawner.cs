
using Avalon.Common.Utils;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Maps;

namespace Avalon.World.Entities;

public interface ICreatureRespawner
{
    void ScheduleRespawn(ICreature creature);
    void Update(TimeSpan deltaTime);
}

public class CreatureRespawner : ICreatureRespawner
{
    private readonly IDictionary<IntervalTimer, ICreature> _respawnTimers = new Dictionary<IntervalTimer, ICreature>();
    private readonly IDictionary<IntervalTimer, ICreature> _removeTimers = new Dictionary<IntervalTimer, ICreature>();
    private readonly IChunk _chunk;

    public CreatureRespawner(IChunk chunk)
    {
        _chunk = chunk;
    }

    public void ScheduleRespawn(ICreature creature)
    {
        var respawnTimer = new IntervalTimer();
        respawnTimer.SetInterval((long)TimeSpan.FromMinutes(3).TotalMilliseconds); //TODO: Respawn timer from creature template metadata
        respawnTimer.SetCurrent(0);
        _respawnTimers.Add(respawnTimer, creature);

        var removeTimer = new IntervalTimer();
        removeTimer.SetInterval((long)TimeSpan.FromMinutes(2).TotalMilliseconds); //TODO: Remove timer from creature template metadata
        removeTimer.SetCurrent(0);
        _removeTimers.Add(removeTimer, creature);
    }

    public void Update(TimeSpan deltaTime)
    {
        var timersToRemove = new List<IntervalTimer>();
        foreach (var (timer, creature) in _removeTimers)
        {
            if (timer.GetCurrent() >= 0)
                timer.Update((long)deltaTime.TotalMilliseconds);

            if (!timer.Passed()) continue;

            _chunk.RemoveCreature(creature);
            timersToRemove.Add(timer);
        }

        foreach (var timer in timersToRemove)
        {
            _removeTimers.Remove(timer);
        }

        var timersToRespawn = new List<IntervalTimer>();

        foreach (var (timer, creature) in _respawnTimers)
        {
            if (timer.GetCurrent() >= 0)
                timer.Update((long)deltaTime.TotalMilliseconds);

            if (!timer.Passed()) continue;

            _chunk.RespawnCreature(creature);
            timersToRespawn.Add(timer);
        }

        foreach (var timer in timersToRespawn)
        {
            _respawnTimers.Remove(timer);
        }

    }
}
