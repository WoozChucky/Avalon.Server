using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Avalon.Api.Contract;
using Avalon.Api.UnitTests.Authentication;
using Avalon.Api.Worlds;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.World.Repositories;
using Avalon.Domain.Auth;
using Avalon.Domain.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;
using AccountAccessLevel = Avalon.Common.Accounts.AccountAccessLevel;
using WorldEntity = Avalon.Domain.Auth.World;

namespace Avalon.Api.UnitTests.Worlds;

/// <summary>
/// /public/world/{worldId}/... over HTTP: anyone reads a world every player may enter, a signed-in caller
/// also reads the worlds they may enter, and everything else is the same empty 404.
/// </summary>
public sealed class PublicRouteShould : IAsyncLifetime
{
    private const ushort Open = 1;   // Player
    private const ushort Staff = 2;  // Admin only
    private const ushort Ptr = 3;    // PTR only
    private const ushort Down = 4;   // Player, databases failed
    private const ushort StaffDown = 5; // Admin only, databases failed

    private readonly IWorldRepository _authWorlds = Substitute.For<IWorldRepository>();
    private readonly IItemTemplateRepository _items = Substitute.For<IItemTemplateRepository>();
    private readonly IAbilityTemplateRepository _abilities = Substitute.For<IAbilityTemplateRepository>();
    private ApiAuthHost _host = null!;

    public async Task InitializeAsync()
    {
        WorldDatabases databases = new(new ushort[] { Open, Staff, Ptr, Down, StaffDown }
            .Select(id => new ConfiguredWorld(new WorldId(id), $"Host=w{id}", $"Host=c{id}")));
        databases.MarkUnavailable(new WorldId(Down));
        databases.MarkUnavailable(new WorldId(StaffDown));

        Row(Open, AccountAccessLevel.Player);
        Row(Staff, AccountAccessLevel.Admin);
        Row(Ptr, AccountAccessLevel.PTR);
        Row(Down, AccountAccessLevel.Player);
        Row(StaffDown, AccountAccessLevel.Admin);

        _items.FindByIdAsync(new ItemTemplateId(12), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new ItemTemplate { Id = new ItemTemplateId(12), Name = "Barkplate Helm" });
        _abilities.FindByIdAsync(new AbilityId(210), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new AbilityTemplate { Id = new AbilityId(210), Name = "Cleave" });

        _host = await ApiAuthHost.StartAsync(configure: services =>
        {
            services.AddWorldDatabases(databases);
            services.AddSingleton(_authWorlds);
            services.AddSingleton(_items);
            services.AddSingleton(_abilities);
        });
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private void Row(ushort id, AccountAccessLevel required) =>
        _authWorlds.FindByIdAsync(Arg.Is<WorldId>(w => w.Value == id), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new WorldEntity
            {
                Id = new WorldId(id), Name = $"World{id}", AccessLevelRequired = required,
                Host = "h", MinVersion = "0.0.1", Version = "0.0.1",
            });

    private Task<HttpResponseMessage> Anonymous(string path) => _host.Client.GetAsync(path);

    private Task<HttpResponseMessage> As(AccountAccessLevel level, string path)
    {
        var account = ApiAuthHost.MakeAccount(level);
        _host.AccountNowIs(account);
        return _host.GetAsync(path, ApiAuthHost.Mint(account));
    }

    [Fact]
    public async Task Serve_an_item_to_anyone_on_a_world_every_player_may_enter()
    {
        HttpResponseMessage response = await Anonymous($"/public/world/{Open}/item/12");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Barkplate Helm", (await response.Content.ReadFromJsonAsync<PublicItemDto>())!.Name);
        Assert.True(response.Headers.CacheControl!.Public);
        Assert.Equal(TimeSpan.FromSeconds(300), response.Headers.CacheControl.MaxAge);
    }

    [Fact]
    public async Task Serve_an_ability_to_anyone_on_a_world_every_player_may_enter()
    {
        HttpResponseMessage response = await Anonymous($"/public/world/{Open}/ability/210");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Cleave", (await response.Content.ReadFromJsonAsync<PublicAbilityDto>())!.Name);
    }

    [Theory]
    [InlineData(Staff)]
    [InlineData(Ptr)]
    public async Task Answer_404_for_an_anonymous_caller_on_a_staff_world(ushort world) =>
        Assert.Equal(HttpStatusCode.NotFound, (await Anonymous($"/public/world/{world}/item/12")).StatusCode);

    [Fact]
    public async Task Serve_a_restricted_world_privately_to_a_caller_who_may_enter_it()
    {
        HttpResponseMessage response = await As(AccountAccessLevel.Admin, $"/public/world/{Staff}/item/12");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.Private);
        Assert.False(response.Headers.CacheControl.Public);
        Assert.Equal(TimeSpan.FromSeconds(300), response.Headers.CacheControl.MaxAge);
    }

    [Fact]
    public async Task Answer_404_for_a_player_on_a_staff_world() =>
        Assert.Equal(HttpStatusCode.NotFound,
            (await As(AccountAccessLevel.Player, $"/public/world/{Staff}/item/12")).StatusCode);

    [Fact]
    public async Task Treat_an_invalid_token_as_anonymous()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/public/world/{Open}/item/12");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-token");

        Assert.Equal(HttpStatusCode.OK, (await _host.Client.SendAsync(request)).StatusCode);
    }

    [Theory]
    [InlineData("/public/world/1/item/99")]
    [InlineData("/public/world/1/ability/99")]
    [InlineData("/public/world/9/item/12")]
    [InlineData("/public/world/01/item/12")]
    public async Task Answer_404_for_an_unknown_id_or_world(string path) =>
        Assert.Equal(HttpStatusCode.NotFound, (await Anonymous(path)).StatusCode);

    [Fact]
    public async Task Answer_503_for_a_world_whose_databases_failed() =>
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Anonymous($"/public/world/{Down}/item/12")).StatusCode);

    [Fact]
    public async Task Answer_404_not_503_for_an_anonymous_caller_on_an_unavailable_staff_world() =>
        Assert.Equal(HttpStatusCode.NotFound, (await Anonymous($"/public/world/{StaffDown}/item/12")).StatusCode);

    [Fact]
    public async Task Still_refuse_anonymous_callers_on_player_world_routes() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await Anonymous($"/world/{Open}/item-template/12")).StatusCode);
}
