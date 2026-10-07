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

    /// <summary>
    /// One creature of <paramref name="template" /> on the ground at <paramref name="near" /> in
    /// <paramref name="instance" /> (item use): snapped as a procedural spawn is (#720), its AI script attached, added
    /// to the instance. Null, placing nothing, when no ground is near or the spawn throws (logged). Tick thread: the
    /// instance is live.
    /// </summary>
    ICreature? SpawnAt(IMapInstance instance, CreatureTemplateId template, Vector3 near);
}

public class CreaturePlacementService : ICreaturePlacementService
{
    // Half-extent (metres) of the random box around a slot center for multi-spawn packs.
    // 1.5 m gives ~3 m diameter, enough to keep individual creatures visually separated
    // without spilling out of the chunk's spawn slot footprint.
    private const float SpawnSpreadRadius = 1.5f;

    // Forest content pass: a leader slot spawns its own creature (a Bramblemaw Alpha) and then its pack.
    private const string LeaderTag = "leader";
    private const string LeaderPackTag = "leader_pack";
    private const string BossTag = "boss";

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
        SpawnTable table = await _spawnTableRepo.FindByIdAsync(cfg.SpawnTableId, track: false, ct)
                    ?? throw new InvalidProceduralConfigException($"SpawnTable {cfg.SpawnTableId.Value} not found");

        var entriesByTag = table.Entries
            .GroupBy(e => e.Tag, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var rng = new Random(seed);

        // Forest content pass: a map with depth bands rolls each creature's level from its piece's band (a set
        // piece's from the highest), and the boss stands at the top of the highest. Without bands nothing below
        // draws a level, so the random stream and every spawn are what they always were.
        IReadOnlyList<ProceduralDepthBand> bands = cfg.DepthBands;
        ushort? bossLevel = DepthBandLevels.BossLevel(bands);

        foreach (PlacedChunk chunk in layout.Chunks)
        {
            ChunkTemplate tpl = _library.GetById(chunk.TemplateId);
            LevelRange? range = DepthBandLevels.For(bands, chunk.Depth, setPiece: chunk.Group is not null);

            foreach (ChunkSpawnSlot slot in tpl.SpawnSlots)
            {
                if (slot.Tag.Equals("empty", StringComparison.OrdinalIgnoreCase)) continue;
                if (slot.Tag.Equals("entry", StringComparison.OrdinalIgnoreCase)) continue;
                if (!entriesByTag.TryGetValue(slot.Tag, out List<SpawnTableEntry>? entries) || entries.Count == 0) continue;

                Vector3 slotCenter = ChunkRotation.LocalToWorld(slot.LocalX, slot.LocalY, slot.LocalZ, chunk.Rotation, layout.CellSize, chunk.WorldPos);
                ushort? fixedLevel = slot.Tag.Equals(BossTag, StringComparison.OrdinalIgnoreCase) ? bossLevel : null;
                PlaceRoll(instance, entries, slotCenter, slot.Tag, cfg.MapTemplateId, rng, range, fixedLevel, spreadAlways: false);

                if (slot.Tag.Equals(LeaderTag, StringComparison.OrdinalIgnoreCase)
                    && entriesByTag.TryGetValue(LeaderPackTag, out List<SpawnTableEntry>? pack) && pack.Count > 0)
                {
                    // The pack spreads around its leader, which stands on the slot's centre.
                    PlaceRoll(instance, pack, slotCenter, LeaderPackTag, cfg.MapTemplateId, rng, range, fixedLevel: null, spreadAlways: true);
                }
            }
        }
    }

    /// <summary>One spawn-table roll at a slot: a weighted entry, a count, then each creature, spread unless alone.</summary>
    private void PlaceRoll(IMapInstance instance, IList<SpawnTableEntry> entries, Vector3 slotCenter, string tag,
        MapTemplateId mapId, Random rng, LevelRange? range, ushort? fixedLevel, bool spreadAlways)
    {
        SpawnTableEntry entry = WeightedPick(entries, rng);
        int count = rng.Next(entry.MinCount, entry.MaxCount + 1);

        for (int i = 0; i < count; i++)
        {
            // Spread multi-spawn packs around the slot center so they don't stack
            // on top of each other. Single-spawn entries (boss) land exactly on center.
            Vector3 spawnPos = count == 1 && !spreadAlways
                ? slotCenter
                : slotCenter + new Vector3(
                    (float)(rng.NextDouble() - 0.5) * 2.0f * SpawnSpreadRadius,
                    0f,
                    (float)(rng.NextDouble() - 0.5) * 2.0f * SpawnSpreadRadius);

            ushort? level = fixedLevel ?? (range is { } r ? (ushort)rng.Next(r.Min, r.Max + 1) : null);
            PlaceProcedural(instance, spawnPos, entry.CreatureId, tag, mapId, level);
        }
    }

    private void PlaceProcedural(IMapInstance instance, Vector3 spawnPos, CreatureTemplateId creatureId, string tag, MapTemplateId mapId, ushort? level)
    {
        // One bad row costs one creature, not the map. SpawnTableEntry rows come from Maps/spawn-tables.json, whose
        // creature ids ChunkCatalogSeeder checks at start-up, but a template can still go away afterwards (a reload),
        // and this runs inside MapInstance construction, where a throw makes the map unenterable for everyone. Same
        // shape as AttachScript's own catch below.
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

            var info = new CreatureInfo
            {
                Position = grounded,
                PrototypeIndex = creatureId.Value,
            };
            ICreature creature = level is { } chosen ? _spawner.Spawn(info, chosen) : _spawner.Spawn(info);
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
                Vector3 position = layout.EntrySpawnWorldPos + new Vector3(spawn.OffsetX, spawn.OffsetY, spawn.OffsetZ);

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

    public ICreature? SpawnAt(IMapInstance instance, CreatureTemplateId template, Vector3 near)
    {
        try
        {
            if (!TryPutOnGround(instance.GetNavigatorForPosition(near), near, out Vector3 position))
            {
                _logger.LogWarning("No ground near {Position} for creature {CreatureId}; it was not spawned", near, template.Value);
                return null;
            }

            ICreature creature = _spawner.Spawn(new CreatureInfo { Position = position, PrototypeIndex = template.Value });
            AttachScript(creature, instance);
            instance.AddCreature(creature);
            return creature;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not spawn creature {CreatureId} at {Position}", template.Value, near);
            return null;
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

        Type? scriptType = _scriptManager.GetAiScript(creature.ScriptName);
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
        foreach (SpawnTableEntry i in items)
        {
            r -= i.Weight;
            if (r <= 0) return i;
        }
        return items[^1];
    }
}
