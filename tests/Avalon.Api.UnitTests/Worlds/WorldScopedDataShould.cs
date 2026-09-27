using System.Net;
using System.Net.Http.Json;
using Avalon.Api.Contract;
using Avalon.Api.UnitTests.Authentication;
using Avalon.Api.Worlds;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.World;
using Avalon.Database.World.Extensions;
using Avalon.Domain.Auth;
using Avalon.Domain.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;
using AccountAccessLevel = Avalon.Common.Accounts.AccountAccessLevel;
using WorldEntity = Avalon.Domain.Auth.World;

namespace Avalon.Api.UnitTests.Worlds;

/// <summary>The same route reads each world's own database (#523): real repositories over two SQLite worlds.</summary>
public sealed class WorldScopedDataShould : IAsyncLifetime
{
    private readonly SqliteWorlds _sqlite = new(1, 2);
    private readonly IWorldRepository _authWorlds = Substitute.For<IWorldRepository>();
    private ApiAuthHost _host = null!;

    public async Task InitializeAsync()
    {
        using (WorldDbContext world2 = _sqlite.CreateWorld(new WorldId(2)))
        {
            world2.ItemTemplates.Single(t => t.Id == new ItemTemplateId(1)).Name = "Asthoria Potion";
            world2.SaveChanges();
        }
        foreach (ushort id in new ushort[] { 1, 2 })
            _authWorlds.FindByIdAsync(Arg.Is<WorldId>(w => w.Value == id), Arg.Any<bool>(), Arg.Any<CancellationToken>())
                .Returns(new WorldEntity
                {
                    Id = new WorldId(id), Name = $"World{id}", AccessLevelRequired = AccountAccessLevel.Player,
                    Host = "h", MinVersion = "0.0.1", Version = "0.0.1",
                });

        _host = await ApiAuthHost.StartAsync(configure: services =>
        {
            services.AddWorldDatabases(new WorldDatabases(
            [
                new ConfiguredWorld(new WorldId(1), "Host=w1", "Host=c1"),
                new ConfiguredWorld(new WorldId(2), "Host=w2", "Host=c2"),
            ]));
            services.AddSingleton<IWorldDbContextFactory>(_sqlite);
            services.AddWorldRepositories();
            services.AddSingleton(_authWorlds);
        });
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        _sqlite.Dispose();
    }

    [Fact]
    public async Task Read_each_worlds_own_rows_for_the_same_route()
    {
        Account account = ApiAuthHost.MakeAccount();
        _host.AccountNowIs(account);
        string token = ApiAuthHost.Mint(account);

        HttpResponseMessage one = await _host.GetAsync("/world/1/item-template/1", token);
        HttpResponseMessage two = await _host.GetAsync("/world/2/item-template/1", token);

        Assert.Equal(HttpStatusCode.OK, one.StatusCode);
        Assert.Equal(HttpStatusCode.OK, two.StatusCode);
        string? nameOne = (await one.Content.ReadFromJsonAsync<ItemTemplateDto>())!.Name;
        Assert.Equal("Asthoria Potion", (await two.Content.ReadFromJsonAsync<ItemTemplateDto>())!.Name);
        Assert.NotEqual("Asthoria Potion", nameOne);
    }
}
