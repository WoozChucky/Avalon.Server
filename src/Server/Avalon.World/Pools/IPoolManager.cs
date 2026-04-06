using Avalon.World.Entities;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Maps;
using Avalon.World.Public.Scripts;
using Avalon.World.Scripts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Pools;

public interface IPoolManager
{
    void SpawnStartingEntities(ISimulationContext context, IReadOnlyList<MapRegion> regions);

    void SpawnEntity(ISimulationContext context, IReadOnlyList<MapRegion> regions, ICreature creature);
}

public class PoolManager : IPoolManager
{
    private readonly ILogger<PoolManager> _logger;
    private readonly ICreatureSpawner _creatureSpawner;
    private readonly IScriptManager _scriptManager;
    private readonly IServiceProvider _serviceProvider;

    public PoolManager(ILoggerFactory loggerFactory, ICreatureSpawner creatureSpawner, IScriptManager scriptManager, IServiceProvider serviceProvider)
    {
        _logger = loggerFactory.CreateLogger<PoolManager>();
        _creatureSpawner = creatureSpawner;
        _scriptManager = scriptManager;
        _serviceProvider = serviceProvider;
    }


    public void SpawnStartingEntities(ISimulationContext context, IReadOnlyList<MapRegion> regions)
    {
        foreach (MapRegion region in regions)
        {
            _logger.LogDebug("Spawning starting entities for region '{RegionName}'", region.Name);

            foreach (var virtualCreature in region.Creatures)
            {
                var creature = _creatureSpawner.Spawn(virtualCreature);

                var scriptType = _scriptManager.GetAiScript(creature.ScriptName);
                if (scriptType is not null)
                {
                    var script = ActivatorUtilities.CreateInstance(_serviceProvider, scriptType, [creature, context]);
                    creature.Script = (AiScript)script;
                }

                context.AddCreature(creature);
            }
        }
    }

    public void SpawnEntity(ISimulationContext context, IReadOnlyList<MapRegion> regions, ICreature creature)
    {
        foreach (MapRegion region in regions)
        {
            foreach (var virtualCreature in region.Creatures)
            {
                if (virtualCreature.Position != creature.Metadata.StartPosition) continue;

                _logger.LogDebug("Spawning entity {PrototypeIndex}", virtualCreature.PrototypeIndex);

                var newCreature = _creatureSpawner.Spawn(virtualCreature);

                var scriptType = _scriptManager.GetAiScript(newCreature.ScriptName);
                if (scriptType is not null)
                {
                    var script = ActivatorUtilities.CreateInstance(_serviceProvider, scriptType, [newCreature, context]);
                    newCreature.Script = (AiScript)script;
                }

                context.AddCreature(newCreature);
                return;
            }
        }
    }
}
