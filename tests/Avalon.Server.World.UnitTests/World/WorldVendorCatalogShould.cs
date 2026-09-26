using Avalon.Database.Auth.Repositories;
using Avalon.Server.World.UnitTests.Vendors;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
using Avalon.World.Maps;
using Avalon.World.Scripts.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.World;

/// <summary>
/// The vendor repository is an optional last World constructor parameter (#432), so the existing
/// call sites that build World need no change. That also means nothing fails to compile if World
/// stops passing it to StaticData. This is what fails then.
/// </summary>
public class WorldVendorCatalogShould
{
    [Fact]
    public async Task Load_the_vendor_catalog_from_the_repository_the_world_was_given()
    {
        var worldRepository = Substitute.For<IWorldRepository>();
        worldRepository.FindByIdAsync(Arg.Any<Avalon.Domain.Auth.WorldId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new Avalon.Domain.Auth.World
            {
                Name = "test", Host = "127.0.0.1", Port = 0, MinVersion = "0.0.1", Version = "1.0.0",
            });

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IChunkLayoutInstanceFactory))
            .Returns(Substitute.For<IChunkLayoutInstanceFactory>());

        TestStaticDataRepositories r = TestStaticData.Repositories(
            items: () => VendorTestData.Items, vendors: VendorRepositories.Of(VendorTestData.Rows));
        var world = new Avalon.World.World(
            NullLoggerFactory.Instance,
            Options.Create(new GameConfiguration { WorldId = new Avalon.Domain.Auth.WorldId(1) }),
            serviceProvider,
            worldRepository,
            Substitute.For<IAvalonMapManager>(),
            Substitute.For<IServiceScopeFactory>(),
            r.CreateInfos, r.ClassStats, r.Items, r.Abilities, r.Levels, r.Creatures, r.BaseStats, r.Rarities,
            r.Texts,
            Substitute.For<IScriptHotReloader>(),
            Substitute.For<IChunkLibrary>(),
            r.Dialogue, r.Loot, r.Vendors);

        await world.LoadAsync(CancellationToken.None);

        Assert.Equal(5, world.Data.Vendors.RowsFor(VendorTestData.Smith).Count);
    }
}
