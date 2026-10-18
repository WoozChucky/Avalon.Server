using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Instances;
using Avalon.World.Loot;
using Avalon.World.Maps.Navigation;
using Avalon.World.Public;
using Avalon.World.Public.Maps;
using Avalon.World.Quests;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Doodads;

/// <summary>
/// Plans and places a procedural map's doodads as its instance is built, off the tick: <see cref="Plan" /> before the
/// navmesh bake, from the layout, the map's rules and the catalog; <see cref="PlaceAsync" /> after it, on the instance
/// being built, before it is published. Nothing here runs on the tick.
/// </summary>
public interface IDoodadPlacementService
{
    /// <summary>
    /// Picks the doodads' slots, required rules first: the same layout seed, rules and catalog always give the same plan.
    /// No config (a town) or no rules plans nothing.
    /// </summary>
    DoodadPlan Plan(ChunkLayout layout, ProceduralMapConfig? config, DoodadCatalog catalog, IChunkLibrary chunks);

    /// <summary>
    /// Places <paramref name="plan" /> in <paramref name="instance" />, built on <paramref name="layout" />'s baked
    /// navmesh: a walk-through doodad snapped to the ground, a solid one at its slot's height, a required one with no slot
    /// left on open navmesh, each with its script's OnSpawn run. One that fails costs that doodad alone (logged).
    /// </summary>
    Task PlaceAsync(MapInstance instance, ChunkLayout layout, DoodadPlan plan, MapTemplateId map, CancellationToken ct);
}

/// <inheritdoc />
/// <param name="scripts">The doodads' scripts, whose OnSpawn runs on each doodad placed.</param>
/// <param name="time">The clock the spawn-mode context's throttled logs read.</param>
public sealed class DoodadPlacementService(DoodadScripts scripts, TimeProvider time, ILoggerFactory loggerFactory)
    : IDoodadPlacementService
{
    /// <summary>Mixed into the layout's seed, so the doodads draw from a stream of their own, apart from the creatures'.</summary>
    public const int DoodadSeedSalt = 0x0D00DAD5;

    /// <summary>The fallback's own stream, apart from the plan's, so a plan never depends on how the fallback drew.</summary>
    private const int FallbackSeedSalt = 0x0FA11BAC;

    /// <summary>No doodad stands nearer the entry spawn than this, in metres on X/Z.</summary>
    public const float EntryClearance = 15f;

    /// <summary>No doodad stands nearer a portal than this.</summary>
    public const float PortalClearance = 8f;

    /// <summary>No doodad stands nearer another doodad than this.</summary>
    public const float DoodadClearance = 2f;

    /// <summary>A fallback point stands at least this far from every navmesh wall.</summary>
    public const float WallClearance = 1.5f;

    /// <summary>How many open navmesh points a required doodad is tried at before it is given up, with an Error.</summary>
    public const int FallbackTries = 64;

    private readonly ILogger<DoodadPlacementService> _logger = loggerFactory.CreateLogger<DoodadPlacementService>();
    private readonly ILogger _contextLogger = loggerFactory.CreateLogger<DoodadUseContext>();

    public DoodadPlan Plan(ChunkLayout layout, ProceduralMapConfig? config, DoodadCatalog catalog, IChunkLibrary chunks)
    {
        if (config is not { DoodadRules.Count: > 0 })
            return DoodadPlan.Empty;

        var rng = new Random(layout.Seed ^ DoodadSeedSalt);
        List<Candidate> candidates = Candidates(layout, chunks);
        bool[] taken = new bool[candidates.Count];
        List<PlannedDoodad> placed = [];
        List<DoodadSpawn> requiredShort = [];
        List<int> pool = [];

        foreach (ProceduralDoodadRule rule in config.DoodadRules.OrderByDescending(r => r.Required).ThenBy(r => r.Sequence))
        {
            if (!catalog.TryGet(rule.DoodadTemplateId, out DoodadTemplateView? template))
            {
                _logger.LogWarning("Doodad rule {Sequence} of map {MapId} names doodad template {DoodadTemplateId}, which the " +
                                   "catalog does not hold; it places nothing", rule.Sequence, config.MapTemplateId.Value,
                    rule.DoodadTemplateId.Value);
                continue;
            }

            int count = rng.Next(rule.MinCount, rule.MaxCount + 1);
            int minDepth = rule.MinDepth ?? 0;
            int maxDepth = rule.MaxDepth ?? int.MaxValue;
            pool.Clear();
            for (int i = 0; i < candidates.Count; i++)
            {
                Candidate c = candidates[i];
                if (!taken[i] && c.Depth >= minDepth && c.Depth <= maxDepth
                    && string.Equals(c.Tag, rule.SlotTag, StringComparison.OrdinalIgnoreCase))
                {
                    pool.Add(i);
                }
            }

            // Fisher-Yates, from the end.
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }

            DoodadGate? visible = Gate(rule.VisibleQuestId, rule.VisibleCondition, rule.VisibleObjectiveId);
            DoodadGate? usable = Gate(rule.UsableQuestId, rule.UsableCondition, rule.UsableObjectiveId);
            int got = 0;
            foreach (int index in pool)
            {
                if (got >= count)
                    break;

                Candidate c = candidates[index];
                if (Crowded(c.Position, template, rule.MinSpacing, placed))
                    continue;

                taken[index] = true;
                placed.Add(new PlannedDoodad(template, c.Position, c.Yaw, AtSlot: true, rule.Required, visible, usable));
                got++;
            }

            if (rule.Required)
            {
                for (int i = got; i < count; i++)
                    requiredShort.Add(new DoodadSpawn(template, Required: true, visible, usable));
            }
        }

        return new DoodadPlan(placed, requiredShort, []);
    }

    public Task PlaceAsync(MapInstance instance, ChunkLayout layout, DoodadPlan plan, MapTemplateId map, CancellationToken ct)
    {
        if (plan.Placed.Count == 0 && plan.RequiredShort.Count == 0)
            return Task.CompletedTask;

        // One context and one random stream for this build alone: a build runs on one thread, and owns its navigator's
        // query until the instance is published.
        var placement = new Placement(instance, layout, map, instance.Quests, new Random(layout.Seed ^ FallbackSeedSalt),
            new DoodadUseContext(time, _contextLogger));
        List<DoodadSpawn> fallback = [.. plan.RequiredShort];

        foreach (PlannedDoodad planned in plan.Placed)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                Vector3 position = planned.Position;
                if (!planned.Template.Solid && !TryPutOnGround(placement.Navigator, planned.Position, out position))
                {
                    if (planned.Required)
                    {
                        fallback.Add(planned.Spawn);
                        continue;
                    }

                    _logger.LogWarning("Doodad {DoodadTemplateId} '{Name}' has no navmesh under its slot at {Position} on map " +
                                       "{MapId}; skipping it", planned.Template.Id.Value, planned.Template.Name, planned.Position,
                        map.Value);
                    continue;
                }

                Place(placement, planned.Spawn, position, planned.YawDegrees);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Could not place doodad {DoodadTemplateId} at {Position} on map {MapId}; skipping it",
                    planned.Template.Id.Value, planned.Position, map.Value);
            }
        }

        foreach (DoodadSpawn spawn in fallback)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                PlaceOnOpenNavmesh(placement, spawn);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Could not place required doodad {DoodadTemplateId} on map {MapId}; skipping it",
                    spawn.Template.Id.Value, map.Value);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Every doodad slot of the layout in world space, in chunk order and each chunk's slots in file order, but those
    /// within <see cref="EntryClearance" /> of the entry spawn or <see cref="PortalClearance" /> of a portal.
    /// </summary>
    private static List<Candidate> Candidates(ChunkLayout layout, IChunkLibrary chunks)
    {
        List<Candidate> candidates = [];
        foreach (PlacedChunk chunk in layout.Chunks)
        {
            foreach (ChunkDoodadSlot slot in chunks.GetById(chunk.TemplateId).DoodadSlots)
            {
                Vector3 world = ChunkRotation.LocalToWorld(slot.LocalX, slot.LocalY, slot.LocalZ, chunk.Rotation,
                    layout.CellSize, chunk.WorldPos);
                if (!Clear(world, layout))
                    continue;

                candidates.Add(new Candidate(world, (slot.Yaw + 90 * chunk.Rotation) % 360, chunk.Depth, slot.Tag));
            }
        }

        return candidates;
    }

    /// <summary>Whether <paramref name="position" /> keeps its distance from the entry spawn and from every portal.</summary>
    private static bool Clear(Vector3 position, ChunkLayout layout)
    {
        if (FlatDistanceSquared(position, layout.EntrySpawnWorldPos) < EntryClearance * EntryClearance)
            return false;

        foreach (PortalPlacement portal in layout.Portals)
        {
            if (FlatDistanceSquared(position, portal.WorldPos) < PortalClearance * PortalClearance)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Whether a doodad of <paramref name="template" /> at <paramref name="position" /> stands nearer than
    /// <paramref name="spacing" /> to one of the same template, or nearer than <see cref="DoodadClearance" /> to any.
    /// </summary>
    private static bool Crowded(Vector3 position, DoodadTemplateView template, float spacing, List<PlannedDoodad> placed)
    {
        foreach (PlannedDoodad other in placed)
        {
            float squared = FlatDistanceSquared(position, other.Position);
            if (squared < DoodadClearance * DoodadClearance)
                return true;
            if (other.Template.Id.Value == template.Id.Value && squared < spacing * spacing)
                return true;
        }

        return false;
    }

    /// <summary>
    /// A required doodad no slot was left for: at a random open navmesh point, clear of walls, the entry, every portal
    /// and every doodad placed, facing yaw 0; after <see cref="FallbackTries" /> points, given up with an Error. A solid
    /// one is never placed off its slot: its box is not in the navmesh, so it would be an unseen wall to every path.
    /// </summary>
    private void PlaceOnOpenNavmesh(Placement placement, DoodadSpawn spawn)
    {
        DoodadTemplateView template = spawn.Template;
        if (template.Solid)
        {
            _logger.LogError("Required doodad {DoodadTemplateId} '{Name}' on map {MapId} (seed {Seed}) found no slot and is " +
                             "solid, so it is not placed on open navmesh", template.Id.Value, template.Name,
                placement.Map.Value, placement.Layout.Seed);
            return;
        }

        if (placement.Navigator is MapNavigator navigator)
        {
            for (int attempt = 0; attempt < FallbackTries; attempt++)
            {
                if (!navigator.TryFindOpenPoint(placement.Random, WallClearance, out Vector3 point)
                    || !Clear(point, placement.Layout) || placement.Crowded(point))
                {
                    continue;
                }

                Place(placement, spawn, point, 0f);
                return;
            }
        }

        _logger.LogError("Required doodad {DoodadTemplateId} '{Name}' found no slot on map {MapId} (seed {Seed}), and no open " +
                         "navmesh point in {Tries} tries; it is not placed", template.Id.Value, template.Name,
            placement.Map.Value, placement.Layout.Seed, FallbackTries);
    }

    /// <summary>One doodad: its gates settled, its script's OnSpawn run (contained), then added to the instance.</summary>
    private void Place(Placement placement, DoodadSpawn spawn, Vector3 position, float yawDegrees)
    {
        DoodadTemplateView template = spawn.Template;
        DoodadGate? visible = spawn.VisibleGate;
        DoodadGate? usable = spawn.UsableGate;
        if (template.Solid && (visible is not null || usable is not null))
        {
            _logger.LogWarning("Doodad {DoodadTemplateId} '{Name}' on map {MapId} is solid, and a solid doodad is never " +
                               "hidden: its spawn's gates are dropped", template.Id.Value, template.Name, placement.Map.Value);
            visible = null;
            usable = null;
        }
        else
        {
            visible = KnownGate(visible, "visibility", template, placement);
            usable = KnownGate(usable, "usability", template, placement);
        }

        var doodad = new Doodad(new ObjectGuid(ObjectType.Doodad, IObject.GenerateId()), template, position, yawDegrees,
            visible, usable);
        RunOnSpawn(placement, doodad);
        placement.Instance.AddDoodad(doodad);
        placement.Standing.Add(position);
    }

    /// <summary>
    /// The spawn's <paramref name="gate" /> when the world's quests hold its quest (and its objective, for OnObjective),
    /// as the catalog would demand; otherwise null, with a Warning, so the template's gate stands.
    /// </summary>
    private DoodadGate? KnownGate(DoodadGate? gate, string kind, DoodadTemplateView template, Placement placement)
    {
        if (gate is not { } g)
            return null;

        if (!placement.Quests.TryGet(g.QuestId, out QuestView? quest))
        {
            _logger.LogWarning("Doodad {DoodadTemplateId} '{Name}' on map {MapId}: its spawn's {Kind} gate names quest " +
                               "{QuestId}, which is not loaded; the template's gate stands", template.Id.Value, template.Name,
                placement.Map.Value, kind, g.QuestId);
            return null;
        }

        bool objectiveOk = g.Condition == DoodadGateCondition.OnObjective
            ? g.ObjectiveId is { } objective && quest.Objective(objective) is not null
            : g.ObjectiveId is null;
        if (!objectiveOk)
        {
            _logger.LogWarning("Doodad {DoodadTemplateId} '{Name}' on map {MapId}: its spawn's {Kind} gate ({Condition}, " +
                               "objective {ObjectiveId}) does not fit quest {QuestId}; the template's gate stands",
                template.Id.Value, template.Name, placement.Map.Value, kind, g.Condition, g.ObjectiveId, g.QuestId);
            return null;
        }

        return g;
    }

    /// <summary>The template's script's OnSpawn, in spawn mode (no host: every live act does nothing), contained.</summary>
    private void RunOnSpawn(Placement placement, Doodad doodad)
    {
        if (scripts.For(doodad.Template) is not { } script)
            return;

        placement.Context.Begin(doodad, host: null, placement.LootRandom, spawning: true);
        try
        {
            script.OnSpawn(placement.Context);
        }
        catch (Exception e)
        {
            scripts.Failed(script.GetType(), nameof(DoodadScript.OnSpawn), e);
        }
        finally
        {
            placement.Context.End();
        }
    }

    /// <summary>
    /// On the navmesh under <paramref name="slot" />, X/Z kept, or on its nearest point just beside it, as a creature's
    /// slot is; false with no mesh within the search box. A map with no navmesh, or a navigator that cannot tell, keeps
    /// the slot's height or samples it.
    /// </summary>
    private static bool TryPutOnGround(IMapNavigator navigator, Vector3 slot, out Vector3 position)
    {
        if (navigator is not IGroundNavigator ground)
        {
            position = slot;
            position.y = navigator.SampleGroundHeight(slot.x, slot.y, slot.z);
            return true;
        }

        return ground.FindGround(slot, out position) != NavmeshGroundKind.None;
    }

    private static DoodadGate? Gate(uint? questId, DoodadGateCondition? condition, uint? objectiveId) =>
        questId is { } quest && condition is { } kind ? new DoodadGate(quest, kind, objectiveId) : null;

    private static float FlatDistanceSquared(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return dx * dx + dz * dz;
    }

    private readonly record struct Candidate(Vector3 Position, int Yaw, int Depth, string Tag);

    /// <summary>One build's placement: its instance, layout and map, the quests its gates are checked against, its draws.</summary>
    private sealed class Placement(MapInstance instance, ChunkLayout layout, MapTemplateId map, QuestCatalog quests,
        Random random, DoodadUseContext context)
    {
        public MapInstance Instance { get; } = instance;
        public ChunkLayout Layout { get; } = layout;
        public MapTemplateId Map { get; } = map;
        public QuestCatalog Quests { get; } = quests;
        public Random Random { get; } = random;
        public ILootRandom LootRandom { get; } = new LootRandom(random);
        public DoodadUseContext Context { get; } = context;
        public IMapNavigator Navigator { get; } = instance.GetNavigatorForPosition(layout.EntrySpawnWorldPos);

        /// <summary>Where every doodad placed so far stands.</summary>
        public List<Vector3> Standing { get; } = [];

        public bool Crowded(Vector3 point)
        {
            foreach (Vector3 other in Standing)
            {
                if (FlatDistanceSquared(point, other) < DoodadClearance * DoodadClearance)
                    return true;
            }

            return false;
        }
    }
}
