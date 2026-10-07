using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Creatures;
using Avalon.World.ChunkLayouts;
using Avalon.World.Entities;
using Avalon.World.Maps.Navigation;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Maps;
using Avalon.World.Scripts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Avalon.Server.World.UnitTests.ChunkLayouts;

/// <summary>
/// #720: a procedural spawn stands on the navmesh, not at its slot's authored height. The slot's X/Z
/// are kept when the mesh is under it; a slot just beside the mesh goes to the mesh's nearest point;
/// a slot with no mesh within the search box places nothing and logs a warning. Against a real baked
/// navmesh: <see cref="CrowdLocomotionShould.FlatNavMesh" />, a flat 40x40 quad at height 0 centred
/// on the origin, eroded by the agent radius at its edges.
/// </summary>
public class ProceduralSpawnGroundShould
{
    /// <summary>A baked mesh's height on flat ground is within a cell height or so of the geometry's.</summary>
    private const float GroundTolerance = 0.25f;

    private static readonly CreatureTemplateId Boar = new(4);

    [Fact]
    public async Task Put_A_Creature_Whose_Slot_Is_Above_The_Ground_On_The_Ground_At_The_Same_X_And_Z()
    {
        ICreatureSpawner spawner = RecordingSpawner(out List<Vector3> placed);
        IMapInstance instance = InstanceOver(FlatGround());

        await BuildService(spawner, NullLoggerFactory.Instance, Slot(5f, 2.5f, 7f))
            .PlaceAsync(instance, Layout(), Config(), seed: 0, CancellationToken.None);

        Vector3 position = Assert.Single(placed);
        Assert.Equal(5f, position.x);
        Assert.Equal(7f, position.z);
        Assert.InRange(position.y, -GroundTolerance, GroundTolerance);
        instance.ReceivedWithAnyArgs(1).AddCreature(default!);
    }

    [Fact]
    public async Task Put_A_Creature_Whose_Slot_Is_Just_Beside_The_Mesh_On_The_Nearest_Point_Of_It()
    {
        // The mesh ends short of x = 20 (the quad's edge, eroded by the agent radius); the slot is
        // half a metre past the quad, within the search box of the mesh.
        ICreatureSpawner spawner = RecordingSpawner(out List<Vector3> placed);
        MapNavigator navigator = FlatGround();

        await BuildService(spawner, NullLoggerFactory.Instance, Slot(20.5f, 1f, 3f))
            .PlaceAsync(InstanceOver(navigator), Layout(), Config(), seed: 0, CancellationToken.None);

        Vector3 position = Assert.Single(placed);
        Assert.InRange(position.x, 18.5f, 20f);
        Assert.Equal(3f, position.z, precision: 2);
        Assert.InRange(position.y, -GroundTolerance, GroundTolerance);
    }

    [Fact]
    public async Task Skip_A_Creature_Whose_Slot_Has_No_Mesh_Within_Reach_With_A_Warning_And_Place_The_Rest()
    {
        ICreatureSpawner spawner = RecordingSpawner(out List<Vector3> placed);
        var log = new TestLog();
        IMapInstance instance = InstanceOver(FlatGround());

        await BuildService(spawner, log, Slot(60f, 0f, 0f), Slot(-4f, 1f, 2f))
            .PlaceAsync(instance, Layout(), Config(), seed: 0, CancellationToken.None);

        Vector3 position = Assert.Single(placed);
        Assert.Equal(-4f, position.x);
        Assert.Equal(2f, position.z);
        instance.ReceivedWithAnyArgs(1).AddCreature(default!);

        (LogLevel Level, Exception? Exception, string Message) warning = Assert.Single(log.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("no navmesh within reach", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Keep_The_Slot_As_It_Is_On_A_Map_With_No_Navmesh()
    {
        // A map whose bake produced nothing: there is no ground to find, and the creature is placed
        // where it always was rather than every creature on the map being skipped.
        ICreatureSpawner spawner = RecordingSpawner(out List<Vector3> placed);

        await BuildService(spawner, NullLoggerFactory.Instance, Slot(5f, 2.5f, 7f))
            .PlaceAsync(InstanceOver(new MapNavigator(NullLoggerFactory.Instance)), Layout(), Config(), seed: 0,
                CancellationToken.None);

        Assert.Equal(new Vector3(5f, 2.5f, 7f), Assert.Single(placed));
    }

    [Fact]
    public async Task Cost_Only_Its_Own_Creature_When_Finding_The_Ground_Throws()
    {
        ICreatureSpawner spawner = RecordingSpawner(out List<Vector3> placed);
        MapNavigator ground = FlatGround();
        IMapInstance instance = Substitute.For<IMapInstance>();
        instance.GetNavigatorForPosition(Arg.Is<Vector3>(p => p.x == 1f))
            .Throws(new InvalidOperationException("navigator broke"));
        instance.GetNavigatorForPosition(Arg.Is<Vector3>(p => p.x != 1f)).Returns(ground);

        await BuildService(spawner, NullLoggerFactory.Instance, Slot(1f, 0f, 1f), Slot(3f, 0f, 3f))
            .PlaceAsync(instance, Layout(), Config(), seed: 0, CancellationToken.None);

        Vector3 position = Assert.Single(placed);
        Assert.Equal(3f, position.x);
        instance.ReceivedWithAnyArgs(1).AddCreature(default!);
    }

    [Fact]
    public async Task Leave_Authored_Spawns_As_They_Were_Snapped_On_The_Mesh_And_At_Their_Height_Off_It()
    {
        // The authored path is unchanged: SampleGroundHeight on the mesh, the authored height where
        // the column is off it (it is never skipped).
        ICreatureSpawner spawner = RecordingSpawner(out List<Vector3> placed);
        IMapCreatureSpawnRepository authored = Substitute.For<IMapCreatureSpawnRepository>();
        authored.FindByMapAsync(Arg.Any<MapTemplateId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<MapCreatureSpawn>>(
            [
                AuthoredRow(1, new Vector3(5f, 2.5f, 7f)),
                AuthoredRow(2, new Vector3(60f, 3f, 0f)),
            ]));

        var service = new CreaturePlacementService(spawner, Substitute.For<IChunkLibrary>(),
            Substitute.For<ISpawnTableRepository>(), authored, Substitute.For<IScriptManager>(),
            Substitute.For<IServiceProvider>(), NullLoggerFactory.Instance);

        await service.PlaceAuthoredAsync(InstanceOver(FlatGround()), Layout(), new MapTemplateId(2),
            CancellationToken.None);

        Assert.Equal(2, placed.Count);
        Assert.Equal(5f, placed[0].x);
        Assert.Equal(7f, placed[0].z);
        Assert.InRange(placed[0].y, -GroundTolerance, GroundTolerance);
        Assert.Equal(new Vector3(60f, 3f, 0f), placed[1]);
    }

    private static MapNavigator FlatGround()
    {
        var navigator = new MapNavigator(NullLoggerFactory.Instance);
        navigator.LoadFromNavMesh(CrowdLocomotionShould.FlatNavMesh.Value);
        return navigator;
    }

    private static IMapInstance InstanceOver(IMapNavigator navigator)
    {
        IMapInstance instance = Substitute.For<IMapInstance>();
        instance.GetNavigatorForPosition(Arg.Any<Vector3>()).Returns(navigator);
        return instance;
    }

    private static ICreatureSpawner RecordingSpawner(out List<Vector3> placed)
    {
        var positions = new List<Vector3>();
        placed = positions;

        ICreatureSpawner spawner = Substitute.For<ICreatureSpawner>();
        uint next = 1;
        spawner.Spawn(Arg.Any<CreatureInfo>()).Returns(ci =>
        {
            positions.Add(ci.Arg<CreatureInfo>().Position);
            ICreature creature = Substitute.For<ICreature>();
            creature.Guid.Returns(new ObjectGuid(ObjectType.Creature, next++));
            creature.Metadata.Returns(Substitute.For<ICreatureMetadata>());
            creature.ScriptName.Returns(string.Empty);
            return creature;
        });
        return spawner;
    }

    /// <summary>A slot at a world position: the one chunk sits at the origin, unrotated, so local is world.</summary>
    private static ChunkSpawnSlot Slot(float x, float y, float z) =>
        new() { Tag = "pack", LocalX = x, LocalY = y, LocalZ = z };

    private static MapCreatureSpawn AuthoredRow(int id, Vector3 offset) => new()
    {
        Id = new MapCreatureSpawnId(id),
        MapTemplateId = new MapTemplateId(2),
        CreatureTemplateId = Boar,
        OffsetX = offset.x,
        OffsetY = offset.y,
        OffsetZ = offset.z,
    };

    private static CreaturePlacementService BuildService(
        ICreatureSpawner spawner, ILoggerFactory loggerFactory, params ChunkSpawnSlot[] slots)
    {
        // One creature per slot, at the slot's centre: a pack of one is never spread.
        var table = new SpawnTable
        {
            Id = new SpawnTableId(1),
            Name = "test",
            Entries =
            [
                new SpawnTableEntry
                {
                    Id = 1, SpawnTableId = new SpawnTableId(1), Tag = "pack", CreatureId = Boar,
                    Weight = 1f, MinCount = 1, MaxCount = 1,
                },
            ],
        };

        ISpawnTableRepository repo = Substitute.For<ISpawnTableRepository>();
        repo.FindByIdAsync(Arg.Any<SpawnTableId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SpawnTable?>(table));

        IChunkLibrary library = Substitute.For<IChunkLibrary>();
        library.GetById(Arg.Any<ChunkTemplateId>()).Returns(new ChunkTemplate
        {
            Id = new ChunkTemplateId(1),
            Name = "test_chunk",
            SpawnSlots = slots.ToList(),
        });

        return new CreaturePlacementService(spawner, library, repo, Substitute.For<IMapCreatureSpawnRepository>(),
            Substitute.For<IScriptManager>(), Substitute.For<IServiceProvider>(), loggerFactory);
    }

    private static ChunkLayout Layout()
    {
        var chunk = new PlacedChunk(new ChunkTemplateId(1), 0, 0, 0, Vector3.zero);

        return new ChunkLayout(
            Seed: 0,
            Chunks: [chunk],
            EntryChunk: chunk,
            BossChunk: null,
            Portals: [],
            EntrySpawnWorldPos: Vector3.zero,
            CellSize: 30f,
            Config: null);
    }

    private static ProceduralMapConfig Config() => new()
    {
        MapTemplateId = new MapTemplateId(2),
        SpawnTableId = new SpawnTableId(1),
    };
}
