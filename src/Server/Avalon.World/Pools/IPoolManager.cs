using Avalon.World.Entities;
using Avalon.World.Maps;
using Avalon.World.Public;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Scripts;
using Avalon.World.Scripts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Pools;

public interface IPoolManager
{
    void SpawnStartingEntities(Chunk chunk);

    void SpawnEntity(Chunk chunk, ICreature creature);
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


    public void SpawnStartingEntities(Chunk chunk)
    {
        _logger.LogDebug("Spawning starting entities for Chunk {ChunkId}", chunk.Id);

        foreach (var virtualCreature in chunk.Metadata.Creatures)
        {
            var creature = _creatureSpawner.Spawn(virtualCreature);

            var scriptType = _scriptManager.GetAiScript(creature.ScriptName);
            if (scriptType is not null)
            {
                var script = ActivatorUtilities.CreateInstance(_serviceProvider, scriptType, [creature, chunk]);
                creature.Script = (AiScript)script;
            }

            chunk.AddCreature(creature);
        }
    }

    public void SpawnEntity(Chunk chunk, ICreature creature)
    {
        foreach (var virtualCreature in chunk.Metadata.Creatures)
        {
            if (virtualCreature.Position != creature.Metadata.StartPosition) continue;

            _logger.LogDebug("Spawning entity {CreatureId} for Chunk {ChunkId}", virtualCreature.PrototypeIndex, chunk.Id);

            var newCreature = _creatureSpawner.Spawn(virtualCreature);

            var scriptType = _scriptManager.GetAiScript(newCreature.ScriptName);
            if (scriptType is not null)
            {
                var script = ActivatorUtilities.CreateInstance(_serviceProvider, scriptType, [newCreature, chunk]);
                newCreature.Script = (AiScript)script;
            }

            chunk.AddCreature(newCreature);
            break;
        }
    }
}
