using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Instances;
using Avalon.World.Public;
using Avalon.World.Public.Enums;
using Avalon.World.Scripts;
using DotRecast.Detour;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.ChunkLayouts;

public class ChunkLayoutInstanceFactoryShould
{
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
        var sp = Substitute.For<IServiceProvider>();
        sp.GetService(typeof(IScriptManager)).Returns(Substitute.For<IScriptManager>());
        sp.GetService(typeof(IWorld)).Returns(Substitute.For<IWorld>());
        return sp;
    }

    [Fact]
    public async Task Build_town_instance_via_predefined_source()
    {
        var template = Town(1);
        var layout = MakeLayout(); // Config = null → predefined town

        var predefinedSource = Substitute.For<IChunkLayoutSource>();
        predefinedSource.BuildAsync(template, Arg.Any<CancellationToken>()).Returns(layout);

        var resolver = Substitute.For<IChunkLayoutSourceResolver>();
        resolver
            .Resolve(template, out Arg.Any<ChunkLayoutSourceKind>())
            .Returns(ci =>
            {
                ci[1] = ChunkLayoutSourceKind.Predefined;
                return predefinedSource;
            });

        var navBuilder = Substitute.For<IChunkLayoutNavmeshBuilder>();
        navBuilder.BuildAsync(layout, Arg.Any<CancellationToken>()).Returns(new DtNavMesh());

        var creaturePlace = Substitute.For<ICreaturePlacementService>();
        var portalPlace = Substitute.For<IPortalPlacementService>();
        var sp = BuildServiceProvider();

        var factory = new ChunkLayoutInstanceFactory(
            NullLoggerFactory.Instance,
            resolver,
            navBuilder,
            creaturePlace,
            portalPlace,
            sp);

        var instance = await factory.BuildAsync(template, ownerCharacterId: null, CancellationToken.None);

        Assert.Equal(template.Id, instance.TemplateId);
        await navBuilder.Received(1).BuildAsync(layout, Arg.Any<CancellationToken>());
        await creaturePlace.DidNotReceive().PlaceAsync(
            Arg.Any<MapInstance>(), Arg.Any<ChunkLayout>(), Arg.Any<ProceduralMapConfig>(),
            Arg.Any<int>(), Arg.Any<CancellationToken>());
        portalPlace.Received(1).Place(instance, layout, null);
    }

    [Fact]
    public async Task Build_procedural_instance_dispatches_creature_placement()
    {
        var template = Normal(2);
        var cfg = new ProceduralMapConfig
        {
            MapTemplateId = template.Id,
            ChunkPoolId = new ChunkPoolId(1),
            SpawnTableId = new SpawnTableId(1),
            MainPathMin = 1, MainPathMax = 1,
            BackPortalTargetMapId = 0,
        };
        var layout = MakeLayout(cfg) with { Seed = 1234 };

        var proceduralSource = Substitute.For<IChunkLayoutSource>();
        proceduralSource.BuildAsync(template, Arg.Any<CancellationToken>()).Returns(layout);

        var resolver = Substitute.For<IChunkLayoutSourceResolver>();
        resolver
            .Resolve(template, out Arg.Any<ChunkLayoutSourceKind>())
            .Returns(ci =>
            {
                ci[1] = ChunkLayoutSourceKind.Procedural;
                return proceduralSource;
            });

        var navBuilder = Substitute.For<IChunkLayoutNavmeshBuilder>();
        navBuilder.BuildAsync(layout, Arg.Any<CancellationToken>()).Returns(new DtNavMesh());

        var creaturePlace = Substitute.For<ICreaturePlacementService>();
        var portalPlace = Substitute.For<IPortalPlacementService>();
        var sp = BuildServiceProvider();

        var factory = new ChunkLayoutInstanceFactory(
            NullLoggerFactory.Instance,
            resolver,
            navBuilder,
            creaturePlace,
            portalPlace,
            sp);

        var instance = await factory.BuildAsync(template, ownerCharacterId: 99u, CancellationToken.None);

        Assert.Equal(template.Id, instance.TemplateId);
        Assert.Equal(1234, instance.Seed);
        await creaturePlace.Received(1).PlaceAsync(
            instance, layout, cfg, 1234, Arg.Any<CancellationToken>());
        portalPlace.Received(1).Place(instance, layout, cfg);
    }
}
