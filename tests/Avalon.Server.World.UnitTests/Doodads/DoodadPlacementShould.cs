using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Handlers;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.Server.World.UnitTests.Quests;
using Avalon.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Doodads;
using Avalon.World.Instances;
using Avalon.World.Maps.Navigation;
using Avalon.World.Testing.Scenarios;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Quests.QuestTestData;

namespace Avalon.Server.World.UnitTests.Doodads;

/// <summary>
/// A procedural map's doodads, planned from its rules over the slots of a layout of committed forest pieces and placed on
/// its baked navmesh: a rule that strays from its tag, depth, count or spacing puts a doodad where no one authored one,
/// and a required doodad that is not placed leaves its quest uncompletable.
/// </summary>
public class DoodadPlacementShould
{
    private const uint Barrel = 1, Shrine = 2, Strongbox = 3, Gated = 4;
    private const uint GateQuest = 1, GateObjective = 101;

    // The entry chunk at (0, 0), then single pieces carrying two 'doodad' slots and one 'doodad_large' each, at depths
    // 1 to 3: (0, 1) at 1, (0, 2) and (1, 1) at 2, (0, 3) at 3.
    private static readonly (string Name, short X, short Z, int Depth)[] s_pieces =
    [
        ("forest_entry_01", 0, 0, 0), ("forest_glade_01", 0, 1, 1), ("forest_rocks_01", 0, 2, 2),
        ("forest_thicket_01", 1, 1, 2), ("forest_ruin_01", 0, 3, 3),
    ];

    private static readonly Lazy<(ChunkLayout Layout, DtNavMesh NavMesh)> s_baked = new(Bake, isThreadSafe: true);

    /// <summary>A gate of the seeded quests other than the board template's: the caravan quest's first step.</summary>
    private static readonly DoodadGate s_rowGate = new(9, DoodadGateCondition.OnObjective, 901);

    private static ScenarioReferenceData Reference => ScenarioReferenceData.Shared;

    [Fact]
    public void Keep_rules_to_their_tags_depths_counts_and_spacing()
    {
        ProceduralMapConfig config = Config(
            Rule(1, Barrel, "doodad", 2, 2, spacing: 25f),
            Rule(2, Shrine, "doodad_large", 1, 1, minDepth: 2));
        DoodadPlacementService service = Service(out _);
        DoodadCatalog catalog = Catalog();
        ChunkLayout baked = s_baked.Value.Layout;
        IChunkLibrary reversed = SlotsReversed(Reference.Chunks);
        List<IReadOnlyList<PlannedDoodad>> plans = [];

        for (int seed = 0; seed < 200; seed++)
        {
            ChunkLayout layout = baked with { Seed = seed };
            DoodadPlan plan = service.Plan(layout, config, catalog, Reference.Chunks);

            var barrels = plan.Placed.Where(p => p.Template.Id.Value == Barrel).ToList();
            Assert.Equal(2, barrels.Count);
            Assert.All(barrels, b => Assert.Contains(Slot(layout, b), s => s.Tag == "doodad"));
            Assert.True(FlatDistance(barrels[0].Position, barrels[1].Position) >= 25f, $"seed {seed}: barrels too close");

            PlannedDoodad shrine = Assert.Single(plan.Placed, p => p.Template.Id.Value == Shrine);
            Assert.Contains(Slot(layout, shrine), s => s.Tag == "doodad_large" && s.Depth >= 2);
            // Each carries its slot's depth band, the level range its drops roll their item level in (#1179).
            Assert.All(plan.Placed, p => Assert.Equal(
                DepthBandLevels.For(config.DepthBands, Slot(layout, p).First().Depth, setPiece: false), p.Levels));
            Assert.NotNull(shrine.Levels);

            Assert.All(plan.Placed, p => Assert.True(FlatDistance(p.Position, layout.EntrySpawnWorldPos) >= 15f));
            Assert.Empty(plan.RequiredShort);

            DoodadPlan again = service.Plan(layout, config, catalog, Reference.Chunks);
            Assert.Equal(plan.Placed, again.Placed);
            // A chunk's slots are owned rows, loaded in no guaranteed order: another order must plan the same doodads.
            Assert.Equal(plan.Placed, service.Plan(layout, config, catalog, reversed).Placed);
            plans.Add(plan.Placed);
        }

        // The same seed gives the same plan, and the seed is what picks it: a plan that ignored it would pass the above.
        Assert.Contains(plans, p => !p.SequenceEqual(plans[0]));
    }

    /// <summary>
    /// A required doodad no slot fits stands on open navmesh, clear of walls, the entry and the portals, and on the
    /// navmesh the entry reaches: the forest's blocker tops bake as islands nothing walks onto, and a strongbox on one is
    /// a quest no one can finish. Over several seeds, so the fallback's draws land in several places.
    /// </summary>
    [Fact]
    public async Task Fall_back_to_open_navmesh_when_no_slot_fits_a_required_rule()
    {
        (ChunkLayout baked, DtNavMesh navMesh) = s_baked.Value;
        ProceduralMapConfig config = Config(Rule(1, Strongbox, "nowhere", 1, 1, required: true));
        var navigator = new MapNavigator(NullLoggerFactory.Instance);
        navigator.LoadFromNavMesh(navMesh);
        var query = new DtNavMeshQuery(navMesh);
        var filter = new DtQueryDefaultFilter();
        var pick = new RcVec3f(0.5f, 2f, 0.5f);
        long[] route = new long[4096];

        for (int seed = 0; seed < 200; seed++)
        {
            ChunkLayout layout = baked with { Seed = seed };
            DoodadPlacementService service = Service(out TestLog log);
            DoodadPlan plan = service.Plan(layout, config, Catalog(), Reference.Chunks);
            Assert.Empty(plan.Placed);
            Assert.Equal(Strongbox, Assert.Single(plan.RequiredShort).Template.Id.Value);

            MapInstance instance = Instance(layout, navMesh, MapInstanceClients.NewWorld());
            await service.PlaceAsync(instance, layout, plan, new MapTemplateId(2), CancellationToken.None);

            Doodad strongbox = Assert.Single(instance.Doodads.All);
            Assert.Empty(log.Errors);
            Assert.Equal(NavmeshGroundKind.Under, navigator.FindGround(strongbox.Position, out _));

            var at = new RcVec3f(strongbox.Position.x, strongbox.Position.y, strongbox.Position.z);
            query.FindNearestPoly(at, pick, filter, out long poly, out _, out _);
            query.FindDistanceToWall(poly, at, 5f, filter, out float toWall, out _, out _);
            Assert.True(toWall >= 1.5f, $"seed {seed}: the strongbox stands {toWall} m from a wall");
            Assert.True(FlatDistance(strongbox.Position, layout.EntrySpawnWorldPos) >= 15f);
            Assert.True(FlatDistance(strongbox.Position, layout.Portals.Single(p => p.Role == PortalRole.Back).WorldPos) >= 8f);

            var entry = new RcVec3f(layout.EntrySpawnWorldPos.x, layout.EntrySpawnWorldPos.y, layout.EntrySpawnWorldPos.z);
            query.FindNearestPoly(entry, pick, filter, out long entryPoly, out _, out _);
            query.FindPath(entryPoly, poly, entry, at, filter, route, out int count, route.Length);
            Assert.True(count > 0 && route[count - 1] == poly,
                $"seed {seed}: the strongbox at {strongbox.Position} stands on navmesh the entry does not reach");
        }
    }

    /// <summary>
    /// A spawn's gate replaces its template's, but one naming a quest the world does not hold is dropped and the
    /// template's stands, and one on a solid template is dropped, so a solid doodad is never hidden: each dropped gate is
    /// one Warning. The template's script's OnSpawn runs on each doodad before it is added. Each keeps its planned
    /// depth band.
    /// </summary>
    [Fact]
    public async Task Place_each_doodad_with_the_gates_it_may_keep_and_its_scripts_OnSpawn()
    {
        (ChunkLayout layout, DtNavMesh navMesh) = s_baked.Value;
        DoodadCatalog catalog = Catalog();
        StaticData data = (await QuestTestWorld.CreateAsync([Quest(GateQuest).WithStage(0, Kill(GateObjective, Boar, 1))])).Data;
        var onObjective = new DoodadGate(GateQuest, DoodadGateCondition.OnObjective, GateObjective);
        var unknownQuest = new DoodadGate(99, DoodadGateCondition.Completed, null);
        Vector3[] spots = layout.Chunks.Where(c => c.Depth > 0).Take(3).Select(c => c.WorldPos + new Vector3(15f, 0f, 15f)).ToArray();
        var plan = new DoodadPlan(
        [
            new PlannedDoodad(Template(catalog, Barrel), spots[0], 90f, VisibleGate: onObjective, Levels: new LevelRange(3, 5)),
            new PlannedDoodad(Template(catalog, Gated), spots[1], 0f, UsableGate: unknownQuest),
            new PlannedDoodad(Template(catalog, Shrine), spots[2], 180f, VisibleGate: onObjective),
        ], [], []);
        DoodadPlacementService service = Service(out TestLog log);
        MapInstance instance = Instance(layout, navMesh, MapInstanceClients.NewWorld(data));

        await service.PlaceAsync(instance, layout, plan, new MapTemplateId(2), CancellationToken.None);

        Assert.Equal(3, instance.Doodads.Count);
        Doodad barrel = instance.Doodads.All[0], gated = instance.Doodads.All[1], shrine = instance.Doodads.All[2];
        Assert.Equal(onObjective, barrel.VisibleGate);
        Assert.Equal(Template(catalog, Gated).Usable, gated.UsableGate);
        Assert.Null(shrine.VisibleGate);
        Assert.Equal(spots[2], shrine.Position);   // a solid doodad keeps its slot's height
        Assert.Equal(2, log.Entries.Count(e => e.Level == LogLevel.Warning));
        Assert.Equal((DoodadState.Ready, DoodadState.Used), (barrel.State, gated.State));
        // The planned depth band is the doodad's own (#1179): its drops roll their item level in it.
        Assert.Equal((new LevelRange(3, 5), (LevelRange?)null), (barrel.Levels, shrine.Levels));
    }

    /// <summary>
    /// Glimmerdell's fixed spawns: the seeded notice board (MapDoodadSpawns row 1) stands at the entry spawn plus its
    /// offset, (9, 24), facing its row's yaw, with its template's usable gate (the row sets none), the Resonance
    /// Workbench (row 2) at (36, 4), and the Lectern of Paths (row 3) at (41.5, 5), facing yaw 225. Then rows over the
    /// seeded templates: a walk-through doodad authored a metre up stands on the navmesh's ground, a solid shrine keeps
    /// its authored height, a solid one off a quarter turn is refused, and a service doodad's row has its gate dropped
    /// (#1179, #1181), so it is never hidden or gated: one Warning each.
    /// </summary>
    [Fact]
    public async Task Place_a_town_doodad_at_its_offset_on_the_ground()
    {
        const uint NoticeBoard = 6, SeededShrine = 3, Lectern = 8;
        ChunkLayout town = Reference.TownLayout;
        var navigator = new MapNavigator(NullLoggerFactory.Instance);
        navigator.LoadFromNavMesh(TownNavmesh.Shared);
        using var database = SqliteDatabase.World();
        IReadOnlyList<MapDoodadSpawn> seeded =
            await new MapDoodadSpawnRepository(database).FindByMapAsync(ScenarioReferenceData.TownMap, CancellationToken.None);
        IMapDoodadSpawnRepository spawns = Substitute.For<IMapDoodadSpawnRepository>();
        spawns.FindByMapAsync(ScenarioReferenceData.TownMap, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<MapDoodadSpawn>>(
        [
            .. seeded,
            TownSpawn(101, NoticeBoard, 0f, 1f, 0f, 0f, usable: s_rowGate),       // at the entry, a metre up
            TownSpawn(102, SeededShrine, -10f, 0.3f, -10f, 180f),
            TownSpawn(103, SeededShrine, 10f, 0f, -10f, 45f),
            TownSpawn(104, Lectern, 0f, 0f, -3f, 0f, usable: s_rowGate),
        ]));
        DoodadPlacementService service = Service(out TestLog log, spawns);
        MapInstance instance = TestMapInstances.Build(MapInstanceClients.NewWorld(Reference.Data), navigator: navigator,
            layout: town, templateId: ScenarioReferenceData.TownMap);

        await service.PlaceAsync(instance, town, DoodadPlan.Empty, ScenarioReferenceData.TownMap, CancellationToken.None);

        Assert.Equal(6, instance.Doodads.Count);
        Doodad board = instance.Doodads.All[0], workbench = instance.Doodads.All[1], lectern = instance.Doodads.All[2],
            lifted = instance.Doodads.All[3], shrine = instance.Doodads.All[4], gatedLectern = instance.Doodads.All[5];
        Assert.Equal(NoticeBoard, board.Template.Id.Value);
        Assert.Equal((9f, 24f), (board.Position.x, board.Position.z));
        Assert.Equal(180f, board.Orientation.y);
        Assert.NotNull(board.UsableGate);
        Assert.Equal(board.Template.Usable, board.UsableGate);
        Assert.Equal(("Resonance Workbench", 36f, 4f), (workbench.Template.Name, workbench.Position.x, workbench.Position.z));
        Assert.Equal(("Lectern of Paths", 41.5f, 5f, 225f),
            (lectern.Template.Name, lectern.Position.x, lectern.Position.z, lectern.Orientation.y));

        Assert.Equal(NavmeshGroundKind.Under, navigator.FindGround(new Vector3(15f, 1f, 15f), out Vector3 ground));
        Assert.Equal(new Vector3(15f, ground.y, 15f), lifted.Position);
        // The row's usable gate replaces the template's (9, 902), in its own column: the template has no visible gate.
        Assert.Equal(s_rowGate, lifted.UsableGate);
        Assert.Null(lifted.VisibleGate);
        Assert.True(ground.y < 0.5f, $"the ground at the entry is {ground.y} m up");
        Assert.Equal(new Vector3(5f, 0.3f, 5f), shrine.Position);
        // The Lectern is walk-through (not solid), so its service alone drops the row's gate.
        Assert.Equal((Lectern, false, null, null), (gatedLectern.Template.Id.Value, gatedLectern.Template.Solid,
            gatedLectern.VisibleGate, gatedLectern.UsableGate));
        Assert.Equal(2, log.Entries.Count(e => e.Level >= LogLevel.Warning));
    }

    private static MapDoodadSpawn TownSpawn(int id, uint template, float x, float y, float z, float yaw,
        DoodadGate? usable = null)
    {
        return new MapDoodadSpawn
        {
            Id = id,
            MapTemplateId = ScenarioReferenceData.TownMap,
            DoodadTemplateId = template,
            OffsetX = x,
            OffsetY = y,
            OffsetZ = z,
            Yaw = yaw,
            UsableQuestId = usable?.QuestId,
            UsableCondition = usable?.Condition,
            UsableObjectiveId = usable?.ObjectiveId,
        };
    }

    /// <summary>The library's chunks with each one's doodad slots in reverse order, as another database load might give them.</summary>
    private static IChunkLibrary SlotsReversed(IChunkLibrary chunks)
    {
        IChunkLibrary reversed = Substitute.For<IChunkLibrary>();
        reversed.GetById(Arg.Any<ChunkTemplateId>()).Returns(call => new ChunkTemplate
        {
            DoodadSlots = [.. chunks.GetById(call.Arg<ChunkTemplateId>()).DoodadSlots.AsEnumerable().Reverse()],
        });
        return reversed;
    }

    /// <summary>The placement, over <paramref name="townSpawns" /> or, without them, a map with no fixed doodad spawns.</summary>
    private static DoodadPlacementService Service(out TestLog log, IMapDoodadSpawnRepository? townSpawns = null)
    {
        log = new TestLog();
        var time = new FakeTimeProvider();
        var scripts = new DoodadScripts(new ServiceCollection().BuildServiceProvider(), time, new Logger<DoodadScripts>(log));
        return new DoodadPlacementService(scripts, time, log, townSpawns ?? FixedDoodadSpawns.None());
    }

    private static ProceduralMapConfig Config(params ProceduralDoodadRule[] rules) => new()
    {
        MapTemplateId = new MapTemplateId(2),
        ChunkPoolId = Reference.ForestConfig.ChunkPoolId,
        SpawnTableId = Reference.ForestConfig.SpawnTableId,
        DepthBands = Reference.ForestConfig.DepthBands,
        DoodadRules = [.. rules],
    };

    private static ProceduralDoodadRule Rule(int sequence, uint template, string tag, byte min, byte max,
        float spacing = 0f, int? minDepth = null, bool required = false)
    {
        return new ProceduralDoodadRule
        {
            Sequence = sequence,
            DoodadTemplateId = new DoodadTemplateId(template),
            SlotTag = tag,
            MinCount = min,
            MaxCount = max,
            Required = required,
            MinSpacing = spacing,
            MinDepth = minDepth,
        };
    }

    /// <summary>Barrel, strongbox and the scripted gated crate walk-through; the shrine solid.</summary>
    private static DoodadCatalog Catalog()
    {
        DoodadTemplate Row(uint id, string name) => new()
        {
            Id = new DoodadTemplateId(id),
            Name = name,
            ModelKit = "props-kit",
            ModelKey = name,
            Interaction = DoodadInteraction.Click,
            InteractionRange = 5f,
            UseLimit = DoodadUseLimit.OncePerInstance,
            SpentLook = DoodadSpentLook.ShowUsed,
        };

        DoodadTemplate shrine = Row(Shrine, "Shrine");
        shrine.Collision = DoodadCollision.Box;
        shrine.CollisionHalfX = 0.6f;
        shrine.CollisionHalfZ = 0.6f;
        shrine.CollisionHeight = 1.4f;
        DoodadTemplate gated = Row(Gated, "Crate");
        gated.ScriptName = nameof(SpentOnSpawnDoodadScript);
        gated.UsableQuestId = GateQuest;
        gated.UsableCondition = DoodadGateCondition.Completed;

        return new DoodadCatalog([Row(Barrel, "Barrel"), shrine, Row(Strongbox, "Strongbox"), gated],
            LootTestData.Catalog(), [], [Quest(GateQuest).WithStage(0, Kill(GateObjective, Boar, 1))],
            name => name == nameof(SpentOnSpawnDoodadScript) ? typeof(SpentOnSpawnDoodadScript) : null,
            NullLoggerFactory.Instance);
    }

    private static DoodadTemplateView Template(DoodadCatalog catalog, uint id) =>
        catalog.TryGet(new DoodadTemplateId(id), out DoodadTemplateView? template) ? template : throw new KeyNotFoundException($"doodad {id}");

    private static MapInstance Instance(ChunkLayout layout, DtNavMesh navMesh, IWorld world)
    {
        var navigator = new MapNavigator(NullLoggerFactory.Instance);
        navigator.LoadFromNavMesh(navMesh);
        return TestMapInstances.Build(world, navigator: navigator, layout: layout, templateId: new MapTemplateId(2));
    }

    /// <summary>The doodad slots of the layout a planned doodad stands at, with their tags and their chunk's depth.</summary>
    private static IEnumerable<(string Tag, int Depth)> Slot(ChunkLayout layout, PlannedDoodad planned) =>
        from chunk in layout.Chunks
        from slot in Reference.Chunks.GetById(chunk.TemplateId).DoodadSlots
        let world = ChunkRotation.LocalToWorld(slot.LocalX, slot.LocalY, slot.LocalZ, chunk.Rotation, layout.CellSize, chunk.WorldPos)
        where FlatDistance(world, planned.Position) < 0.01f
        select (slot.Tag, chunk.Depth);

    private static float FlatDistance(Vector3 a, Vector3 b) => MathF.Sqrt((a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z));

    private static (ChunkLayout, DtNavMesh) Bake()
    {
        IReadOnlyList<ChunkPoolMember> pool = Reference.Chunks.GetByPool(Reference.ForestConfig.ChunkPoolId);
        const float Cell = 30f;
        var chunks = s_pieces.Select(p => new PlacedChunk(
            pool.Single(m => m.Template.Name == p.Name).Template.Id, p.X, p.Z, 0, new Vector3(p.X * Cell, 0f, p.Z * Cell), p.Depth)).ToList();
        ChunkTemplate entry = pool.Single(m => m.Template.Name == "forest_entry_01").Template;
        ChunkSpawnSlot entrySlot = entry.SpawnSlots.Single(s => s.Tag == "entry");
        ChunkPortalSlot back = entry.PortalSlots.Single(p => p.Role == PortalRole.Back);
        var layout = new ChunkLayout(0, chunks, chunks[0], null,
            [new PortalPlacement(PortalRole.Back, new Vector3(back.LocalX, back.LocalY, back.LocalZ), 1)],
            new Vector3(entrySlot.LocalX, entrySlot.LocalY, entrySlot.LocalZ), Cell);

        DtNavMesh navMesh = ChunkLayoutNavmeshBuilder.ForTesting(NullLoggerFactory.Instance, Reference.Chunks,
            Path.GetDirectoryName(TownNavmesh.ContentRoot)!).BuildAsync(layout, CancellationToken.None).GetAwaiter().GetResult();
        return (layout, navMesh);
    }
}

/// <summary>Spends its doodad as it is placed: what OnSpawn may do, off the tick, before the instance is published.</summary>
public sealed class SpentOnSpawnDoodadScript : DoodadScript
{
    public override void OnSpawn(IDoodadContext ctx) => ctx.SetState(DoodadState.Used);

    public override void OnUse(IDoodadUseContext ctx)
    {
    }
}
