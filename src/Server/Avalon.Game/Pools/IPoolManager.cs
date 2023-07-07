using Avalon.Game.Creatures;
using Avalon.Game.Maps;
using Avalon.Game.Scripts;
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
    private readonly IAIController _aiController;

    public PoolManager(ILogger<PoolManager> logger, ICreatureSpawner creatureSpawner, IAIController aiController)
    {
        _logger = logger;
        _creatureSpawner = creatureSpawner;
        _aiController = aiController;
    }


    public void SpawnStartingEntities(MapInstance instance)
    {
        _logger.LogInformation("Spawning starting entities for map {MapId}", instance.MapId);

        foreach (var virtualCreature in instance.VirtualizedMap.Creatures)
        {
            var creature = _creatureSpawner.Spawn(virtualCreature);
            
            var scriptType = _aiController.GetScriptTemplate(creature.ScriptName);
            if (scriptType is not null)
            {
                var script = scriptType.GetConstructor(new[] {typeof(Creature), typeof(MapInstance)})?.Invoke(new object[] {creature, instance});
                if (script is not null)
                {
                    creature.Script = (AIScript) script;
                }
            }
            
            instance.AddCreature(creature);
        }
    }

    public void Update(TimeSpan deltaTime, MapInstance instance)
    {
        
    }
}
