using Avalon.Common.ValueObjects;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.World.Public.Enums;
using Avalon.World.Respawn;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Respawn;

public class RespawnTargetResolverShould
{
    private static MapTemplate Template(int id, MapType type) => new()
    {
        Id = new MapTemplateId((ushort)id),
        Name = $"map_{id}",
        Description = string.Empty,
        MapType = type,
    };

    private static IServiceProvider Sp(
        IMapTemplateRepository mapRepo,
        IProceduralMapConfigRepository configRepo)
    {
        var sp = Substitute.For<IServiceProvider>();
        var scope = Substitute.For<IServiceScope>();
        var inner = Substitute.For<IServiceProvider>();
        scope.ServiceProvider.Returns(inner);
        inner.GetService(typeof(IMapTemplateRepository)).Returns(mapRepo);
        inner.GetService(typeof(IProceduralMapConfigRepository)).Returns(configRepo);
        var factory = Substitute.For<IServiceScopeFactory>();
        factory.CreateScope().Returns(scope);
        sp.GetService(typeof(IServiceScopeFactory)).Returns(factory);
        return sp;
    }

    [Fact]
    public async Task Return_current_map_when_already_in_a_town()
    {
        var maps = Substitute.For<IMapTemplateRepository>();
        maps.FindByIdAsync(new MapTemplateId(1), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Template(1, MapType.Town));

        var resolver = new RespawnTargetResolver(NullLoggerFactory.Instance,
            Sp(maps, Substitute.For<IProceduralMapConfigRepository>()));

        var result = await resolver.ResolveTownAsync(new MapTemplateId(1), CancellationToken.None);

        Assert.Equal((ushort)1, result.Value);
    }

    [Fact]
    public async Task Walk_back_portal_chain_until_a_town_is_found()
    {
        var maps = Substitute.For<IMapTemplateRepository>();
        maps.FindByIdAsync(new MapTemplateId(3), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Template(3, MapType.Normal));
        maps.FindByIdAsync(new MapTemplateId(2), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Template(2, MapType.Normal));
        maps.FindByIdAsync(new MapTemplateId(1), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Template(1, MapType.Town));

        var configs = Substitute.For<IProceduralMapConfigRepository>();
        configs.FindByTemplateIdAsync(new MapTemplateId(3), Arg.Any<CancellationToken>())
            .Returns(new ProceduralMapConfig { MapTemplateId = new MapTemplateId(3), BackPortalTargetMapId = 2 });
        configs.FindByTemplateIdAsync(new MapTemplateId(2), Arg.Any<CancellationToken>())
            .Returns(new ProceduralMapConfig { MapTemplateId = new MapTemplateId(2), BackPortalTargetMapId = 1 });

        var resolver = new RespawnTargetResolver(NullLoggerFactory.Instance, Sp(maps, configs));

        var result = await resolver.ResolveTownAsync(new MapTemplateId(3), CancellationToken.None);

        Assert.Equal((ushort)1, result.Value);
    }

    [Fact]
    public async Task Fall_back_to_map_id_1_when_chain_breaks()
    {
        var maps = Substitute.For<IMapTemplateRepository>();
        maps.FindByIdAsync(new MapTemplateId(2), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Template(2, MapType.Normal));

        var configs = Substitute.For<IProceduralMapConfigRepository>();
        configs.FindByTemplateIdAsync(Arg.Any<MapTemplateId>(), Arg.Any<CancellationToken>())
            .Returns((ProceduralMapConfig?)null);

        var resolver = new RespawnTargetResolver(NullLoggerFactory.Instance, Sp(maps, configs));

        var result = await resolver.ResolveTownAsync(new MapTemplateId(2), CancellationToken.None);

        Assert.Equal((ushort)1, result.Value);
    }

    [Fact]
    public async Task Bail_after_eight_hops_to_break_a_cycle()
    {
        var maps = Substitute.For<IMapTemplateRepository>();
        maps.FindByIdAsync(Arg.Any<MapTemplateId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(ci => Template(((MapTemplateId)ci[0]).Value, MapType.Normal));

        var configs = Substitute.For<IProceduralMapConfigRepository>();
        configs.FindByTemplateIdAsync(Arg.Any<MapTemplateId>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var id = ((MapTemplateId)ci[0]).Value;
                return new ProceduralMapConfig
                {
                    MapTemplateId = new MapTemplateId(id),
                    BackPortalTargetMapId = (ushort)((id % 4) + 2),
                };
            });

        var resolver = new RespawnTargetResolver(NullLoggerFactory.Instance, Sp(maps, configs));

        var result = await resolver.ResolveTownAsync(new MapTemplateId(2), CancellationToken.None);

        Assert.Equal((ushort)1, result.Value); // bailed-out fallback
    }
}
