using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using Avalon.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
using Avalon.World.Doodads;
using Avalon.World.Instances;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Enums;
using Avalon.World.Scripts;
using DotRecast.Detour;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.ChunkLayouts;

public class ChunkLayoutInstanceFactoryShould
{
    [Theory]
    [InlineData("authored")]
    [InlineData("doodads")]
    [InlineData("portals")]
    [InlineData("cancelled")]
    public async Task Release_the_encoded_layout_when_placement_fails(string stage)
    {
        MapTemplate template = Town(1);
        ChunkLayout layout = MakeLayout();
        IChunkLayoutSource source = Substitute.For<IChunkLayoutSource>();
        source.BuildAsync(template, Arg.Any<CancellationToken>()).Returns(layout);
        IChunkLayoutSourceResolver resolver = Substitute.For<IChunkLayoutSourceResolver>();
        resolver.Resolve(template, out Arg.Any<ChunkLayoutSourceKind>()).Returns(call =>
        {
            call[1] = ChunkLayoutSourceKind.Predefined;
            return source;
        });
        IChunkLayoutNavmeshBuilder nav = Substitute.For<IChunkLayoutNavmeshBuilder>();
        nav.BuildAsync(layout, Arg.Any<CancellationToken>()).Returns(new DtNavMesh());
        ICreaturePlacementService creatures = Substitute.For<ICreaturePlacementService>();
        IDoodadPlacementService doodads = Substitute.For<IDoodadPlacementService>();
        doodads.Plan(layout, layout.Config, Arg.Any<DoodadCatalog>(), Arg.Any<IChunkLibrary>())
            .Returns(DoodadPlan.Empty);
        IPortalPlacementService portals = Substitute.For<IPortalPlacementService>();
        var pool = new PayloadSegmentPool(countOutstanding: true);
        IServiceProvider services = BuildServiceProvider();
        services.GetService(typeof(PacketEncoder)).Returns(new PacketEncoder(pool));
        Exception failure = stage == "cancelled" ? new OperationCanceledException() : new InvalidOperationException(stage);

        creatures.PlaceAuthoredAsync(Arg.Any<MapInstance>(), layout, template.Id, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Assert.Equal(1, pool.Outstanding);
                return stage is "authored" or "cancelled" ? Task.FromException(failure) : Task.CompletedTask;
            });
        if (stage == "doodads")
        {
            doodads.PlaceAsync(Arg.Any<MapInstance>(), layout, DoodadPlan.Empty, template.Id, Arg.Any<CancellationToken>())
                .Returns(Task.FromException(failure));
        }
        if (stage == "portals")
            portals.When(p => p.Place(Arg.Any<MapInstance>(), layout, null)).Do(_ => throw failure);
        var factory = new ChunkLayoutInstanceFactory(NullLoggerFactory.Instance, resolver, nav, creatures,
            portals, doodads, services);

        Exception? actual = await Record.ExceptionAsync(() => factory.BuildAsync(template, null, CancellationToken.None));

        Assert.Same(failure, actual);
        Assert.Equal(0, pool.Outstanding);
    }

    private static MapTemplate Town(ushort id) => new()
    {
        Id = new MapTemplateId(id),
        MapType = MapType.Town,
        Name = $"town_{id}",
        Description = string.Empty,
    };

    private static MapTemplate Normal(ushort id) => new()
    {
        Id = new MapTemplateId(id),
        MapType = MapType.Normal,
        Name = $"normal_{id}",
        Description = string.Empty,
    };

    private static ChunkLayout MakeLayout(ProceduralMapConfig? cfg = null)
    {
        var entry = new PlacedChunk(new ChunkTemplateId(1), 0, 0, 0, Vector3.zero);
        return new ChunkLayout(
            Seed: 0,
            Chunks: new[] { entry },
            EntryChunk: entry,
            BossChunk: null,
            Portals: Array.Empty<PortalPlacement>(),
            EntrySpawnWorldPos: Vector3.zero,
            CellSize: 30f,
            Config: cfg);
    }

    private static IServiceProvider BuildServiceProvider()
    {
        IServiceProvider sp = Substitute.For<IServiceProvider>();
        sp.GetService(typeof(IScriptManager)).Returns(Substitute.For<IScriptManager>());
        IWorld world = Substitute.For<IWorld>();
        world.Configuration.Returns(new GameConfiguration());
        sp.GetService(typeof(IWorld)).Returns(world);
        sp.GetService(typeof(CombatConfig)).Returns(new CombatConfig());
        sp.GetService(typeof(IChunkLibrary)).Returns(new AnyChunkLibrary());
        return sp;
    }

    [Fact]
    public async Task Build_town_instance_via_predefined_source()
    {
        MapTemplate template = Town(1);
        ChunkLayout layout = MakeLayout(); // Config = null → predefined town

        IChunkLayoutSource predefinedSource = Substitute.For<IChunkLayoutSource>();
        predefinedSource.BuildAsync(template, Arg.Any<CancellationToken>()).Returns(layout);

        IChunkLayoutSourceResolver resolver = Substitute.For<IChunkLayoutSourceResolver>();
        resolver
            .Resolve(template, out Arg.Any<ChunkLayoutSourceKind>())
            .Returns(ci =>
            {
                ci[1] = ChunkLayoutSourceKind.Predefined;
                return predefinedSource;
            });

        IChunkLayoutNavmeshBuilder navBuilder = Substitute.For<IChunkLayoutNavmeshBuilder>();
        navBuilder.BuildAsync(layout, Arg.Any<CancellationToken>()).Returns(new DtNavMesh());

        ICreaturePlacementService creaturePlace = Substitute.For<ICreaturePlacementService>();
        IPortalPlacementService portalPlace = Substitute.For<IPortalPlacementService>();
        IDoodadPlacementService doodadPlace = Substitute.For<IDoodadPlacementService>();
        doodadPlace.Plan(layout, layout.Config, Arg.Any<DoodadCatalog>(), Arg.Any<IChunkLibrary>()).Returns(DoodadPlan.Empty);
        IServiceProvider sp = BuildServiceProvider();

        var factory = new ChunkLayoutInstanceFactory(
            NullLoggerFactory.Instance,
            resolver,
            navBuilder,
            creaturePlace,
            portalPlace,
            doodadPlace,
            sp);

        using MapInstance instance = await factory.BuildAsync(template, ownerCharacterId: null, CancellationToken.None);

        Assert.Equal(template.Id, instance.TemplateId);
        await navBuilder.Received(1).BuildAsync(layout, Arg.Any<CancellationToken>());
        await creaturePlace.DidNotReceive().PlaceAsync(
            Arg.Any<MapInstance>(), Arg.Any<ChunkLayout>(), Arg.Any<ProceduralMapConfig>(),
            Arg.Any<int>(), Arg.Any<CancellationToken>());

        // The wiring that puts NPCs in town. Nothing else calls PlaceAuthoredAsync, so without this
        // assertion the whole authored-spawn path could be deleted from BuildAsync and every test
        // covering the placement logic itself would stay green.
        await creaturePlace.Received(1).PlaceAuthoredAsync(
            instance, layout, template.Id, Arg.Any<CancellationToken>());

        portalPlace.Received(1).Place(instance, layout, null);

        // Doodads are planned before the bake and placed on the built instance, for every layout kind (a town's plan
        // is empty until its fixed spawns join it).
        await doodadPlace.Received(1).PlaceAsync(instance, layout, DoodadPlan.Empty, template.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Build_procedural_instance_dispatches_creature_placement()
    {
        MapTemplate template = Normal(2);
        var cfg = new ProceduralMapConfig
        {
            MapTemplateId = template.Id,
            ChunkPoolId = new ChunkPoolId(1),
            SpawnTableId = new SpawnTableId(1),
            MainPathMin = 1,
            MainPathMax = 1,
            BackPortalTargetMapId = 0,
        };
        ChunkLayout layout = MakeLayout(cfg) with { Seed = 1234 };

        IChunkLayoutSource proceduralSource = Substitute.For<IChunkLayoutSource>();
        proceduralSource.BuildAsync(template, Arg.Any<CancellationToken>()).Returns(layout);

        IChunkLayoutSourceResolver resolver = Substitute.For<IChunkLayoutSourceResolver>();
        resolver
            .Resolve(template, out Arg.Any<ChunkLayoutSourceKind>())
            .Returns(ci =>
            {
                ci[1] = ChunkLayoutSourceKind.Procedural;
                return proceduralSource;
            });

        IChunkLayoutNavmeshBuilder navBuilder = Substitute.For<IChunkLayoutNavmeshBuilder>();
        navBuilder.BuildAsync(layout, Arg.Any<CancellationToken>()).Returns(new DtNavMesh());

        ICreaturePlacementService creaturePlace = Substitute.For<ICreaturePlacementService>();
        IPortalPlacementService portalPlace = Substitute.For<IPortalPlacementService>();
        IDoodadPlacementService doodadPlace = Substitute.For<IDoodadPlacementService>();
        doodadPlace.Plan(layout, layout.Config, Arg.Any<DoodadCatalog>(), Arg.Any<IChunkLibrary>()).Returns(DoodadPlan.Empty);
        IServiceProvider sp = BuildServiceProvider();

        var factory = new ChunkLayoutInstanceFactory(
            NullLoggerFactory.Instance,
            resolver,
            navBuilder,
            creaturePlace,
            portalPlace,
            doodadPlace,
            sp);

        using MapInstance instance = await factory.BuildAsync(template, ownerCharacterId: 99u, CancellationToken.None);

        Assert.Equal(template.Id, instance.TemplateId);
        Assert.Equal(1234, instance.Seed);
        await creaturePlace.Received(1).PlaceAsync(
            instance, layout, cfg, 1234, Arg.Any<CancellationToken>());

        // Authored placement is not town-only — it runs for every layout kind, alongside the
        // spawn-table roll. Procedural maps simply have no authored rows today.
        await creaturePlace.Received(1).PlaceAuthoredAsync(
            instance, layout, template.Id, Arg.Any<CancellationToken>());

        portalPlace.Received(1).Place(instance, layout, cfg);
        Received.InOrder(() =>
        {
            doodadPlace.Plan(layout, cfg, Arg.Any<DoodadCatalog>(), Arg.Any<IChunkLibrary>());
            navBuilder.BuildAsync(layout, Arg.Any<CancellationToken>());
            doodadPlace.PlaceAsync(instance, layout, DoodadPlan.Empty, template.Id, Arg.Any<CancellationToken>());
        });
    }
}
