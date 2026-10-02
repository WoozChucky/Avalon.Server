using System.Net.Http.Json;
using Avalon.Api.Contract;
using Avalon.Api.Controllers;
using Avalon.Api.UnitTests.Authentication;
using Avalon.Api.Worlds;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;
using AccountAccessLevel = Avalon.Common.Accounts.AccountAccessLevel;
using WorldEntity = Avalon.Domain.Auth.World;

namespace Avalon.Api.UnitTests.Worlds;

/// <summary>GET /public/world: the worlds a caller may read tooltips from, and the default one.</summary>
public sealed class PublicWorldListShould
{
    private readonly IWorldRepository _authWorlds = Substitute.For<IWorldRepository>();

    private async Task<ApiAuthHost> Start(ushort? defaultWorld)
    {
        WorldDatabases databases = new(new ushort[] { 1, 2, 3 }
            .Select(id => new ConfiguredWorld(new WorldId(id), $"Host=w{id}", $"Host=c{id}")));
        Row(1, "Development", AccountAccessLevel.Admin);
        Row(2, "Asthoria", AccountAccessLevel.Player);
        Row(3, "Public Test Realm", AccountAccessLevel.PTR);

        return await ApiAuthHost.StartAsync(configure: services =>
        {
            services.AddWorldDatabases(databases);
            services.AddSingleton(_authWorlds);
            services.AddSingleton(new PublicWorldSettings(defaultWorld));
        });
    }

    private void Row(ushort id, string name, AccountAccessLevel required) =>
        _authWorlds.FindByIdAsync(Arg.Is<WorldId>(w => w.Value == id), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new WorldEntity
            {
                Id = new WorldId(id), Name = name, AccessLevelRequired = required,
                Host = "h", MinVersion = "0.0.1", Version = "0.0.1",
            });

    [Fact]
    public async Task List_only_the_worlds_every_player_may_enter_for_an_anonymous_caller()
    {
        await using ApiAuthHost host = await Start(defaultWorld: 2);

        PublicWorldsDto dto = (await host.Client.GetFromJsonAsync<PublicWorldsDto>("/public/world"))!;

        Assert.Equal((ushort?)2, dto.DefaultWorldId);
        Assert.Equal(["Asthoria"], dto.Worlds.Select(w => w.Name));
    }

    [Fact]
    public async Task Never_be_shared_by_a_cache()
    {
        await using ApiAuthHost host = await Start(defaultWorld: 2);

        HttpResponseMessage response = await host.Client.GetAsync("/public/world");

        Assert.True(response.Headers.CacheControl!.Private);
        Assert.True(response.Headers.CacheControl.NoCache);
    }

    [Fact]
    public async Task List_every_world_a_signed_in_caller_may_enter()
    {
        await using ApiAuthHost host = await Start(defaultWorld: 2);
        var admin = ApiAuthHost.MakeAccount(AccountAccessLevel.Admin);
        host.AccountNowIs(admin);

        HttpResponseMessage response = await host.GetAsync("/public/world", ApiAuthHost.Mint(admin));
        PublicWorldsDto dto = (await response.Content.ReadFromJsonAsync<PublicWorldsDto>())!;

        // AccessLevels.ForWorld(PTR) is PTR | GameMaster, and the GameMaster mask includes Admin.
        Assert.Equal(["Development", "Asthoria", "Public Test Realm"], dto.Worlds.Select(w => w.Name));
        Assert.Equal((ushort?)2, dto.DefaultWorldId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData((ushort)1)]
    public async Task Fall_back_to_the_first_readable_world(ushort? configured)
    {
        await using ApiAuthHost host = await Start(defaultWorld: configured);

        PublicWorldsDto dto = (await host.Client.GetFromJsonAsync<PublicWorldsDto>("/public/world"))!;

        Assert.Equal((ushort?)2, dto.DefaultWorldId);
    }
}
