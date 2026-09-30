using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.World.Entities;
using Avalon.World.Maps.Navigation;
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
    /// <summary>
    /// Rolls <paramref name="cfg"/>'s spawn table against every chunk spawn slot in the layout.
    /// Procedural maps only — it needs both a config and slots, and a town has neither.
    /// </summary>
    Task PlaceAsync(IMapInstance instance, ChunkLayout layout, ProceduralMapConfig cfg, int seed, CancellationToken ct);

    /// <summary>
    /// Places the map's authored <see cref="MapCreatureSpawn"/> rows — hand-placed creatures at
    /// fixed offsets from the entry point. Runs for every map, procedural or predefined; a map with
    /// no rows places nothing. This is the only path that puts creatures in a town.
    /// </summary>
    Task PlaceAuthoredAsync(IMapInstance instance, ChunkLayout layout, MapTemplateId mapTemplateId, CancellationToken ct);
}

public class CreaturePlacementService : ICreaturePlacementService
{
    // Half-extent (metres) of the random box around a slot center for multi-spawn packs.
    // 1.5 m gives ~3 m diameter, enough to keep individual creatures visually separated
    // without spilling out of the chunk's spawn slot footprint.
    private const float SpawnSpreadRadius = 1.5f;

    private readonly ICreatureSpawner _spawner;
    private readonly IChunkLibrary _library;
    private readonly ISpawnTableRepository _spawnTableRepo;
    private readonly IMapCreatureSpawnRepository _authoredSpawnRepo;
    private readonly IScriptManager _scriptManager;
    private readonly IServiceProvider _sp;
    private readonly ILogger<CreaturePlacementService> _logger;

    public CreaturePlacementService(
        ICreatureSpawner spawner,
        IChunkLibrary library,
        ISpawnTableRepository spawnTableRepo,
        IMapCreatureSpawnRepository authoredSpawnRepo,
        IScriptManager scriptManager,
        IServiceProvider sp,
        ILoggerFactory loggerFactory)
    {
        _spawner = spawner;
        _library = library;
        _spawnTableRepo = spawnTableRepo;
        _authoredSpawnRepo = authoredSpawnRepo;
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
                var slotCenter = ChunkRotation.LocalToWorld(slot.LocalX, slot.LocalY, slot.LocalZ, chunk.Rotation, layout.CellSize, chunk.WorldPos);

                for (int i = 0; i < count; i++)
                {
                    // Spread multi-spawn packs around the slot center so they don't stack
                    // on top of each other. Single-spawn entries (boss) land exactly on center.
                    var spawnPos = count == 1
                        ? slotCenter
                        : slotCenter + new Vector3(
                            (float)(rng.NextDouble() - 0.5) * 2.0f * SpawnSpreadRadius,
                            0f,
                            (float)(rng.NextDouble() - 0.5) * 2.0f * SpawnSpreadRadius);

                    PlaceProcedural(instance, spawnPos, entry.CreatureId, slot.Tag, cfg.MapTemplateId);
                }
            }
        }
    }

    private void PlaceProcedural(
        IMapInstance instance, Vector3 spawnPos, CreatureTemplateId creatureId, string tag, MapTemplateId mapId)
    {
        // One bad row costs one creature, not the map. SpawnTableEntry rows are migration
        // SQL rather than model seed data, so a mistyped CreatureId cannot be caught by a
        // seed test — and this runs inside MapInstance construction, where a throw makes
        // the map unenterable for everyone. Same shape as AttachScript's own catch below.
        try
        {
            // #720: the slot's height only centres the ground search, as an authored spawn's
            // OffsetY does; a creature left at it floats, and its home (where a fight starts) is
            // off the mesh.
            if (!TryPutOnGround(instance.GetNavigatorForPosition(spawnPos), spawnPos, out Vector3 grounded))
            {
                _logger.LogWarning(
                    "Spawn slot '{Tag}' puts creature {CreatureId} at {Position} on map {MapId}, with no navmesh within reach; skipping it",
                    tag, creatureId, spawnPos, mapId);
                return;
            }

            var creature = _spawner.Spawn(new CreatureInfo
            {
                Position = grounded,
                PrototypeIndex = creatureId.Value,
            });
            AttachScript(creature, instance);
            instance.AddCreature(creature);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Could not place creature {CreatureId} for slot tag '{Tag}' on map {MapId}; skipping it",
                creatureId, tag, mapId);
        }
    }

    public async Task PlaceAuthoredAsync(
        IMapInstance instance, ChunkLayout layout, MapTemplateId mapTemplateId, CancellationToken ct)
    {
        IReadOnlyCollection<MapCreatureSpawn> spawns = await _authoredSpawnRepo.FindByMapAsync(mapTemplateId, ct);

        foreach (MapCreatureSpawn spawn in spawns)
        {
            // Same bargain as the procedural path above: one bad row costs one creature, not the
            // map. This runs inside MapInstance construction, and for town that means one mistyped
            // CreatureTemplateId would otherwise lock every player out of the starting zone.
            try
            {
                // Rows store offsets from the entry point rather than world coordinates — see
                // MapCreatureSpawn's remarks for why.
                var position = layout.EntrySpawnWorldPos + new Vector3(spawn.OffsetX, spawn.OffsetY, spawn.OffsetZ);

                // OffsetY only centres the navmesh search box; the navmesh decides the real height.
                // SampleGroundHeight returns the y it was given when the column is off-mesh, so an
                // unreachable spot leaves the authored height in place rather than dropping the NPC
                // through the floor.
                position.y = instance.GetNavigatorForPosition(position)
                    .SampleGroundHeight(position.x, position.y, position.z);

                ICreature creature = _spawner.Spawn(new CreatureInfo
                {
                    Position = position,
                    PrototypeIndex = spawn.CreatureTemplateId.Value
                });

                // CreatureInfo carries no orientation, so facing is applied after the spawn.
                creature.Orientation = new Vector3(0f, spawn.Facing, 0f);

                // Before the script, so a patrol script finds its route from the first tick.
                if (spawn.Path is { Points.Count: > 0 } path)
                {
                    creature.PatrolPath = ToWorldPath(path, layout.EntrySpawnWorldPos, instance);
                }

                AttachScript(creature, instance);
                instance.AddCreature(creature);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Could not place authored creature {CreatureId} (spawn {SpawnId}) on map {MapId}; skipping it",
                    spawn.CreatureTemplateId, spawn.Id, mapTemplateId);
            }
        }
    }

    /// <summary>
    /// Where a procedural spawn stands (#720): on the navmesh under <paramref name="slot" />, X/Z kept, or,
    /// when the slot is just beside the mesh (a pack's spread pushed past its edge), on the mesh's nearest
    /// point; false when no mesh is within the search box, and the creature is not placed. A map with no
    /// navmesh keeps the slot as it is, as before. A navigator that cannot say whether it found ground
    /// gets the authored path's snap, <see cref="IMapNavigator.SampleGroundHeight" />.
    /// </summary>
    private static bool TryPutOnGround(IMapNavigator navigator, Vector3 slot, out Vector3 position)
    {
        if (navigator is not IGroundNavigator groundNavigator)
        {
            position = slot;
            position.y = navigator.SampleGroundHeight(slot.x, slot.y, slot.z);
            return true;
        }

        return groundNavigator.FindGround(slot, out position) != NavmeshGroundKind.None;
    }

    /// <summary>
    /// A path's points in walk order, as world positions snapped to the navmesh the same way the
    /// spawn itself is: offsets from the entry point, with OffsetY only centring the height search.
    /// </summary>
    private static List<PatrolPoint> ToWorldPath(CreaturePath path, Vector3 entry, IMapInstance instance)
    {
        var points = new List<PatrolPoint>(path.Points.Count);

        foreach (CreaturePathPoint point in path.Points.OrderBy(p => p.Sequence))
        {
            Vector3 position = entry + new Vector3(point.OffsetX, point.OffsetY, point.OffsetZ);
            position.y = instance.GetNavigatorForPosition(position)
                .SampleGroundHeight(position.x, position.y, position.z);

            points.Add(new PatrolPoint(position, TimeSpan.FromMilliseconds(point.WaitMs)));
        }

        return points;
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
