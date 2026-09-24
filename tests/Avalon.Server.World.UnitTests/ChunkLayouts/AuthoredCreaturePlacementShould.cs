using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Entities;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Maps;
using Avalon.World.Scripts;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.ChunkLayouts;

/// <summary>
/// Authored spawns — hand-placed creatures, as opposed to the procedural path's spawn tables rolled
/// against chunk slots. This is the only way a creature reaches a town, whose predefined layout has
/// neither spawn slots nor a spawn table.
/// </summary>
public class AuthoredCreaturePlacementShould
{
    private static readonly MapTemplateId TownMap = new(1);

    [Fact]
    public async Task Place_Each_Authored_Row_At_Its_Offset_From_The_Maps_Entry_Point()
    {
        // Rows store offsets from the entry spawn, not absolute coordinates: a town's world
        // coordinates fall out of its MapChunkPlacement rows and the cell size, so an absolute
        // position would drift silently the moment the layout changed.
        ICreature uriel = StubCreature(1);
        ICreature innkeeper = StubCreature(2);

        var spawner = Substitute.For<ICreatureSpawner>();
        spawner.Spawn(Arg.Is<CreatureInfo>(i => i.PrototypeIndex == 1)).Returns(uriel);
        spawner.Spawn(Arg.Is<CreatureInfo>(i => i.PrototypeIndex == 2)).Returns(innkeeper);

        IMapInstance instance = StubInstance(groundHeight: 12f);

        await BuildService(spawner,
                Row(1, creature: 1, offset: new Vector3(-3f, 0f, 4f), facing: 143f),
                Row(2, creature: 2, offset: new Vector3(0f, 0f, 7f), facing: 180f))
            .PlaceAuthoredAsync(instance, LayoutEnteringAt(new Vector3(100f, 5f, 200f)), TownMap, CancellationToken.None);

        // Entry (100, 5, 200) + offset, with Y replaced by the sampled ground height.
        spawner.Received(1).Spawn(Arg.Is<CreatureInfo>(i =>
            i.Position.x == 97f && i.Position.y == 12f && i.Position.z == 204f));
        spawner.Received(1).Spawn(Arg.Is<CreatureInfo>(i =>
            i.Position.x == 100f && i.Position.y == 12f && i.Position.z == 207f));

        instance.Received(1).AddCreature(uriel);
        instance.Received(1).AddCreature(innkeeper);
    }

    [Fact]
    public async Task Snap_The_Spawn_To_The_Navmesh_Rather_Than_Trusting_The_Authored_Height()
    {
        // OffsetY is a hint for the vertical search box, not the answer. Without the snap an NPC
        // authored at the wrong height hovers or sinks, and the value is hand-written seed data.
        ICreature npc = StubCreature(1);
        var spawner = Substitute.For<ICreatureSpawner>();
        spawner.Spawn(Arg.Any<CreatureInfo>()).Returns(npc);

        IMapInstance instance = StubInstance(groundHeight: 41.5f);

        await BuildService(spawner, Row(1, creature: 1, offset: new Vector3(0f, 0f, 0f), facing: 0f))
            .PlaceAuthoredAsync(instance, LayoutEnteringAt(Vector3.zero), TownMap, CancellationToken.None);

        spawner.Received(1).Spawn(Arg.Is<CreatureInfo>(i => i.Position.y == 41.5f));
    }

    [Fact]
    public async Task Face_The_Creature_The_Way_The_Row_Says()
    {
        // CreatureInfo carries no orientation, so facing has to be applied after the spawn. Without
        // it every NPC in town stares north.
        ICreature npc = StubCreature(1);
        var spawner = Substitute.For<ICreatureSpawner>();
        spawner.Spawn(Arg.Any<CreatureInfo>()).Returns(npc);

        await BuildService(spawner, Row(1, creature: 1, offset: Vector3.zero, facing: 217f))
            .PlaceAuthoredAsync(StubInstance(0f), LayoutEnteringAt(Vector3.zero), TownMap, CancellationToken.None);

        Assert.Equal(217f, npc.Orientation.y);
    }

    [Fact]
    public async Task Skip_A_Row_Whose_Template_Is_Missing_Rather_Than_Failing_The_Whole_Map()
    {
        // Same reasoning as the procedural path: placement runs inside MapInstance construction, so
        // a throw makes the town unenterable for everyone over one mistyped id.
        ICreature good = StubCreature(2);
        var spawner = Substitute.For<ICreatureSpawner>();
        spawner.Spawn(Arg.Is<CreatureInfo>(i => i.PrototypeIndex == 999))
            .Returns(_ => throw new Exception("Could not find creature template 999"));
        spawner.Spawn(Arg.Is<CreatureInfo>(i => i.PrototypeIndex == 2)).Returns(good);

        IMapInstance instance = StubInstance(0f);

        await BuildService(spawner,
                Row(1, creature: 999, offset: Vector3.zero, facing: 0f),
                Row(2, creature: 2, offset: Vector3.zero, facing: 0f))
            .PlaceAuthoredAsync(instance, LayoutEnteringAt(Vector3.zero), TownMap, CancellationToken.None);

        instance.Received(1).AddCreature(good);
    }

    [Fact]
    public async Task Place_Nothing_On_A_Map_With_No_Authored_Rows()
    {
        // Every procedural map hits this path too, and none of them has authored rows today.
        var spawner = Substitute.For<ICreatureSpawner>();
        IMapInstance instance = StubInstance(0f);

        await BuildService(spawner)
            .PlaceAuthoredAsync(instance, LayoutEnteringAt(Vector3.zero), TownMap, CancellationToken.None);

        spawner.DidNotReceiveWithAnyArgs().Spawn(default(CreatureInfo)!);
        instance.DidNotReceiveWithAnyArgs().AddCreature(default!);
    }

    private static MapCreatureSpawn Row(int id, ulong creature, Vector3 offset, float facing) => new()
    {
        Id = new MapCreatureSpawnId(id),
        MapTemplateId = TownMap,
        CreatureTemplateId = new CreatureTemplateId(creature),
        OffsetX = offset.x,
        OffsetY = offset.y,
        OffsetZ = offset.z,
        Facing = facing
    };

    private static ICreature StubCreature(ulong id)
    {
        ICreature creature = Substitute.For<ICreature>();
        creature.Guid.Returns(new ObjectGuid(ObjectType.Creature, (uint)id));
        creature.Metadata.Returns(Substitute.For<ICreatureMetadata>());
        creature.ScriptName.Returns(string.Empty);
        return creature;
    }

    private static IMapInstance StubInstance(float groundHeight)
    {
        var navigator = Substitute.For<IMapNavigator>();
        navigator.SampleGroundHeight(Arg.Any<float>(), Arg.Any<float>(), Arg.Any<float>())
            .Returns(groundHeight);

        var instance = Substitute.For<IMapInstance>();
        instance.GetNavigatorForPosition(Arg.Any<Vector3>()).Returns(navigator);
        return instance;
    }

    private static ChunkLayout LayoutEnteringAt(Vector3 entry)
    {
        var chunk = new PlacedChunk(new ChunkTemplateId(1), 0, 0, 0, Vector3.zero);

        return new ChunkLayout(
            Seed: 0,
            Chunks: [chunk],
            EntryChunk: chunk,
            BossChunk: null,
            Portals: [],
            EntrySpawnWorldPos: entry,
            CellSize: 30f,
            Config: null);
    }

    private static CreaturePlacementService BuildService(ICreatureSpawner spawner, params MapCreatureSpawn[] rows)
    {
        var authored = Substitute.For<IMapCreatureSpawnRepository>();
        authored.FindByMapAsync(Arg.Any<MapTemplateId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<MapCreatureSpawn>>(rows));

        return new CreaturePlacementService(
            spawner,
            Substitute.For<IChunkLibrary>(),
            Substitute.For<ISpawnTableRepository>(),
            authored,
            Substitute.For<IScriptManager>(),
            Substitute.For<IServiceProvider>(),
            NullLoggerFactory.Instance);
    }
}
