using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.World;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Doodads;
using Avalon.World.Instances;
using Avalon.World.Maps.Navigation;
using Avalon.World.Public;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Enums;
using Avalon.World.Reload;
using Avalon.World.Respawn;
using Avalon.World.Scripts;
using Avalon.World.Testing.Scenarios;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Doodads;

/// <summary>
/// A solid doodad is a wall in the server's navmesh and in the map the client is sent, or the two disagree: the server
/// stops a player the client lets through, or lets one walk through a fountain. The instance is built by the real
/// factory, over the real forest_path_01 obj, its doodads planned and the mesh baked as the World server does it.
/// </summary>
public class SolidDoodadNavmeshShould
{
    private const uint Fountain = 1;
    private const float HalfX = 1.2f, HalfZ = 0.6f, Height = 1.8f;

    /// <summary>The path's middle, a metre up as ChunkGen writes every doodad slot; the entry spawn is in a corner.</summary>
    private static readonly Vector3 s_slotAt = new(15f, 1f, 15f);

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    public async Task Stop_a_walk_at_a_solid_doodad_and_send_its_box_with_the_layout(int yaw)
    {
        MapInstance instance = await Build(yaw);

        (float halfX, float halfZ) = yaw == 90 ? (HalfZ, HalfX) : (HalfX, HalfZ);
        var box = new DoodadObstacle(s_slotAt.x - halfX, s_slotAt.x + halfX, s_slotAt.z - halfZ, s_slotAt.z + halfZ, s_slotAt.y,
            s_slotAt.y + Height);

        // East along the path's middle, straight at the box's west face: the eroded mesh ends an agent radius short of
        // it, give or take a voxel.
        Vector3 stop = instance.GetNavigatorForPosition(s_slotAt)
            .RaycastWalkable(new Vector3(5f, 0.1f, s_slotAt.z), new Vector3(25f, 0.1f, s_slotAt.z));
        Assert.InRange(stop.x, box.MinX - (NavmeshBuildSettings.AgentRadius + 0.3f), box.MinX);

        var sent = new List<OutboundPacket>();
        IWorldConnection connection = Substitute.For<IWorldConnection>();
        connection.When(c => c.Send(Arg.Any<OutboundPacket>())).Do(ci => sent.Add(ci.Arg<OutboundPacket>()));
        MapArrival.SendLayout(connection, instance, instance.TemplateId.Value);

        SChunkLayoutPacket layout = Assert.Single(TestTown.Read<SChunkLayoutPacket>(sent, NetworkPacketType.SMSG_CHUNK_LAYOUT));
        DoodadObstacleDto obstacle = Assert.Single(layout.Obstacles);
        Assert.Equal(box, new DoodadObstacle(obstacle.MinX, obstacle.MaxX, obstacle.MinZ, obstacle.MaxZ, obstacle.BaseY,
            obstacle.TopY));
    }

    private static async Task<MapInstance> Build(int yaw)
    {
        var template = new MapTemplate
        {
            Id = new MapTemplateId(2),
            MapType = MapType.Normal,
            Name = "forest",
            Description = string.Empty,
        };
        var config = new ProceduralMapConfig
        {
            MapTemplateId = template.Id,
            ChunkPoolId = new ChunkPoolId(1),
            SpawnTableId = new SpawnTableId(1),
            DoodadRules =
            [
                new ProceduralDoodadRule
                {
                    Sequence = 1, DoodadTemplateId = new DoodadTemplateId(Fountain), SlotTag = "doodad_large", MinCount = 1,
                    MaxCount = 1,
                },
            ],
        };
        var path = new PlacedChunk(new ChunkTemplateId(1), 0, 0, 0, Vector3.zero);
        var layout = new ChunkLayout(7, [path], path, null, [], new Vector3(2f, 0f, 2f), 30f, config);
        var chunks = new PathChunk(yaw);

        IChunkLayoutSource source = Substitute.For<IChunkLayoutSource>();
        source.BuildAsync(template, Arg.Any<CancellationToken>()).Returns(layout);
        IChunkLayoutSourceResolver resolver = Substitute.For<IChunkLayoutSourceResolver>();
        resolver.Resolve(template, out Arg.Any<ChunkLayoutSourceKind>()).Returns(ci =>
        {
            ci[1] = ChunkLayoutSourceKind.Procedural;
            return source;
        });

        StaticData data = await TestStaticData.LoadAsync();
        data.Apply(new DoodadsPatch(Catalog()));
        IWorld world = MapInstanceClients.NewWorld(data);
        IServiceProvider services = Substitute.For<IServiceProvider>();
        services.GetService(typeof(IWorld)).Returns(world);
        services.GetService(typeof(IChunkLibrary)).Returns(chunks);
        services.GetService(typeof(IScriptManager)).Returns(Substitute.For<IScriptManager>());
        services.GetService(typeof(CombatConfig)).Returns(new CombatConfig());

        var factory = new ChunkLayoutInstanceFactory(NullLoggerFactory.Instance, resolver,
            ChunkLayoutNavmeshBuilder.ForTesting(NullLoggerFactory.Instance, chunks, Path.GetDirectoryName(TownNavmesh.ContentRoot)!),
            Substitute.For<ICreaturePlacementService>(), Substitute.For<IPortalPlacementService>(),
            new DoodadPlacementService(new DoodadScripts(new ServiceCollection().BuildServiceProvider(), TimeProvider.System,
                NullLogger<DoodadScripts>.Instance), TimeProvider.System, NullLoggerFactory.Instance),
            services);

        MapInstance instance = await factory.BuildAsync(template, ownerCharacterId: 1, CancellationToken.None);
        Assert.Equal(s_slotAt, Assert.Single(instance.Doodads.All).Position);
        return instance;
    }

    private static DoodadCatalog Catalog() => new(
        [
            new DoodadTemplate
            {
                Id = new DoodadTemplateId(Fountain),
                Name = "Fountain",
                ModelKit = "props-kit",
                ModelKey = "Fountain",
                Interaction = DoodadInteraction.Click,
                InteractionRange = 5f,
                UseLimit = DoodadUseLimit.OncePerInstance,
                SpentLook = DoodadSpentLook.ShowUsed,
                Collision = DoodadCollision.Box,
                CollisionHalfX = HalfX,
                CollisionHalfZ = HalfZ,
                CollisionHeight = Height,
            },
        ],
        LootTestData.Catalog(), [], [], static _ => null, NullLoggerFactory.Instance);

    /// <summary>forest_path_01, with one doodad_large slot at its middle facing <paramref name="yaw" />.</summary>
    private sealed class PathChunk(int yaw) : IChunkLibrary
    {
        public ChunkTemplate GetById(ChunkTemplateId id) => new()
        {
            Id = id,
            Name = "forest_path_01",
            CellSize = 30f,
            DoodadSlots = [new ChunkDoodadSlot { Tag = "doodad_large", LocalX = s_slotAt.x, LocalY = s_slotAt.y, LocalZ = s_slotAt.z, Yaw = yaw }],
        };

        public Task LoadAsync(CancellationToken ct) => Task.CompletedTask;

        public IReadOnlyList<ChunkPoolMember> GetByPool(ChunkPoolId poolId) => throw new NotSupportedException();

        public IReadOnlyDictionary<ChunkTemplateId, ChunkTemplate> LookupByIds(IEnumerable<ChunkTemplateId> ids) =>
            throw new NotSupportedException();

        public IReadOnlyList<ChunkGroupDefinition> GetGroupsByPool(ChunkPoolId poolId) => throw new NotSupportedException();
    }
}
