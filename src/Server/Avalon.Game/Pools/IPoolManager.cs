using Avalon.Game.Creatures;
using Avalon.Game.Maps;
using Microsoft.Extensions.Logging;

namespace Avalon.Game.Pools;

public interface IPoolManager
{
    void SpawnStartingEntities(MapInstance instance);
    void Update(TimeSpan deltaTime, MapInstance instance);
}

public class PoolManager : IPoolManager
{
    private readonly ILogger<PoolManager> _logger;
    private readonly ICreatureSpawner _creatureSpawner;

    public PoolManager(ILogger<PoolManager> logger, ICreatureSpawner creatureSpawner)
    {
        _logger = logger;
        _creatureSpawner = creatureSpawner;
    }


    public void SpawnStartingEntities(MapInstance instance)
    {
        _logger.LogInformation("Spawning starting entities for map {MapId}", instance.MapId);

        foreach (var virtualCreature in instance.VirtualizedMap.Creatures)
        {
            instance.AddCreature(_creatureSpawner.Spawn(virtualCreature.TemplateId));
        }
    }

    public void Update(TimeSpan deltaTime, MapInstance instance)
    {
        
    }
}
