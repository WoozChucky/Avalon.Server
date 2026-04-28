using Avalon.Common.Mathematics;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.World.Entities;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Maps;
using Avalon.World.Public.Scripts;
using Avalon.World.Scripts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Avalon.World.ChunkLayouts;

public interface ICreaturePlacementService
{
    Task PlaceAsync(IMapInstance instance, ChunkLayout layout, ProceduralMapConfig cfg, int seed, CancellationToken ct);
}

public class CreaturePlacementService : ICreaturePlacementService
{
    private readonly ICreatureSpawner _spawner;
    private readonly IChunkLibrary _library;
    private readonly ISpawnTableRepository _spawnTableRepo;
    private readonly IScriptManager _scriptManager;
    private readonly IServiceProvider _sp;
    private readonly ILogger<CreaturePlacementService> _logger;

    public CreaturePlacementService(
        ICreatureSpawner spawner,
        IChunkLibrary library,
        ISpawnTableRepository spawnTableRepo,
        IScriptManager scriptManager,
        IServiceProvider sp,
        ILoggerFactory loggerFactory)
    {
        _spawner = spawner;
        _library = library;
        _spawnTableRepo = spawnTableRepo;
        _scriptManager = scriptManager;
        _sp = sp;
        _logger = loggerFactory.CreateLogger<CreaturePlacementService>();
    }

    public async Task PlaceAsync(IMapInstance instance, ChunkLayout layout, ProceduralMapConfig cfg, int seed, CancellationToken ct)
    {
        var table = await _spawnTableRepo.FindByIdAsync(cfg.SpawnTableId, track: false, ct)
                    ?? throw new InvalidProceduralConfigException($"SpawnTable {cfg.SpawnTableId.Value} not found");

        var entriesByTag = table.Entries
            .GroupBy(e => e.Tag, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var rng = new Random(seed);

        foreach (var chunk in layout.Chunks)
        {
            var tpl = _library.GetById(chunk.TemplateId);
            foreach (var slot in tpl.SpawnSlots)
            {
                if (slot.Tag.Equals("empty", StringComparison.OrdinalIgnoreCase)) continue;
                if (slot.Tag.Equals("entry", StringComparison.OrdinalIgnoreCase)) continue;
                if (!entriesByTag.TryGetValue(slot.Tag, out var entries) || entries.Count == 0) continue;

                var entry = WeightedPick(entries, rng);
                int count = rng.Next(entry.MinCount, entry.MaxCount + 1);
                var worldPos = ChunkRotation.LocalToWorld(slot.LocalX, slot.LocalY, slot.LocalZ, chunk.Rotation, layout.CellSize, chunk.WorldPos);

                for (int i = 0; i < count; i++)
                {
                    var creatureInfo = new CreatureInfo
                    {
                        Position = worldPos,
                        PrototypeIndex = entry.CreatureId.Value,
                    };
                    var creature = _spawner.Spawn(creatureInfo);
                    AttachScript(creature, instance);
                    instance.AddCreature(creature);
                }
            }
        }
    }

    private void AttachScript(ICreature creature, IMapInstance instance)
    {
        if (string.IsNullOrWhiteSpace(creature.ScriptName)) return;

        var scriptType = _scriptManager.GetAiScript(creature.ScriptName);
        if (scriptType is null)
        {
            _logger.LogWarning("AI script '{ScriptName}' not found for creature {Id}", creature.ScriptName, creature.Guid);
            return;
        }

        try
        {
            creature.Script = ActivatorUtilities.CreateInstance(_sp, scriptType, creature, instance) as AiScript;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to construct AI script '{ScriptName}' for creature {Id}", creature.ScriptName, creature.Guid);
        }
    }

    private static SpawnTableEntry WeightedPick(IList<SpawnTableEntry> items, Random rng)
    {
        float total = items.Sum(i => i.Weight);
        float r = (float)(rng.NextDouble() * total);
        foreach (var i in items)
        {
            r -= i.Weight;
            if (r <= 0) return i;
        }
        return items[^1];
    }
}
