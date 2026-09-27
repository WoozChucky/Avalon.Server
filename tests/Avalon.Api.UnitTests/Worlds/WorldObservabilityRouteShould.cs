using System.Net;
using System.Net.Http.Json;
using Avalon.Api.Contract;
using Avalon.Api.Services;
using Avalon.Api.UnitTests.Authentication;
using Avalon.Api.Worlds;
using Avalon.Database;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Presence;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;
using AccountAccessLevel = Avalon.Common.Accounts.AccountAccessLevel;
using WorldEntity = Avalon.Domain.Auth.World;

namespace Avalon.Api.UnitTests.Worlds;

/// <summary>
/// Per-character presence lives under /world/{worldId}/observability/character/{id} (#556), behind the
/// #523 world check, and reads only that world's presence key: character ids are unique only per world.
/// </summary>
public sealed class WorldObservabilityRouteShould : IAsyncLifetime
{
    private const ushort One = 1;         // configured, available, Player
    private const ushort Two = 2;         // configured, available, Player
    private const ushort Down = 3;        // configured, unavailable, Player
    private const ushort Staff = 4;       // configured, available, Admin only

    private static readonly Guid InstanceOne = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid InstanceTwo = Guid.Parse("00000000-0000-0000-0000-0000000000a2");

    private readonly IReplicatedCache _cache = Substitute.For<IReplicatedCache>();
    private readonly IWorldRepository _authWorlds = Substitute.For<IWorldRepository>();
    private readonly List<WorldEntity> _rows = [];
    private ApiAuthHost _host = null!;

    public async Task InitializeAsync()
    {
        WorldDatabases databases = new(new[] { One, Two, Down, Staff }
            .Select(id => new ConfiguredWorld(new WorldId(id), $"Host=w{id}", $"Host=c{id}")));
        databases.MarkUnavailable(new WorldId(Down));

        Row(One, AccountAccessLevel.Player);
        Row(Two, AccountAccessLevel.Player);
        Row(Down, AccountAccessLevel.Player);
        Row(Staff, AccountAccessLevel.Admin);
        _authWorlds.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(_rows);

        // Character 7 is online in every world: ids are unique only per world.
        Presence(One, InstanceOne, "Nym");
        Presence(Two, InstanceTwo, "Zed");
        Presence(Down, InstanceOne, "Down");
        Presence(Staff, InstanceOne, "Staff");

        _host = await ApiAuthHost.StartAsync(_cache, services =>
        {
            services.AddWorldDatabases(databases);
            services.AddSingleton(_authWorlds);
            services.AddSingleton(Substitute.For<IWorldRepositories>());
            services.AddMemoryCache();
            services.AddScoped<IObservabilityService, ObservabilityService>();
        });
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private void Row(ushort id, AccountAccessLevel required)
    {
        var row = new WorldEntity
        {
            Id = new WorldId(id), Name = $"World{id}", AccessLevelRequired = required,
            Host = "h", MinVersion = "0.0.1", Version = "0.0.1",
        };
        _rows.Add(row);
        _authWorlds.FindByIdAsync(Arg.Is<WorldId>(w => w.Value == id), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(row);
    }

    private void Presence(ushort world, Guid instance, string name)
    {
        var character = new CharacterPresenceSnapshot(7, name, "Wizard", 0f, 0f, 0f, 0f, 1, 1, 1, "Idle", false, false,
            DateTime.UtcNow);
        _cache.GetAsync(CacheKeys.WorldPresence(world)).Returns(PresenceJson.Serialize(new WorldPresenceSnapshot(
            world, DateTime.UtcNow,
            [new InstancePresenceSnapshot(instance, 12, 0, "Normal", "", null, [character])])));
        _cache.GetAsync(CacheKeys.CharacterPresenceIndex(world, 7))
            .Returns(PresenceJson.Serialize(new CharacterPresenceIndex(world, instance)));
    }

    private Task<HttpResponseMessage> Get(string path)
    {
        Account account = ApiAuthHost.MakeAccount(AccountAccessLevel.GameMaster);
        _host.AccountNowIs(account);
        return _host.GetAsync(path, ApiAuthHost.Mint(account));
    }

    [Fact]
    public async Task Answer_with_the_named_worlds_character_and_never_another_worlds()
    {
        HttpResponseMessage two = await Get("/world/2/observability/character/7");
        HttpResponseMessage one = await Get("/world/1/observability/character/7");

        Assert.Equal(HttpStatusCode.OK, two.StatusCode);
        PlayerPresenceDto presenceTwo = (await two.Content.ReadFromJsonAsync<PlayerPresenceDto>())!;
        Assert.Equal(("Zed", Two, InstanceTwo),
            (presenceTwo.Target.Name, presenceTwo.Instance.WorldId, presenceTwo.Instance.InstanceId));
        Assert.Equal(HttpStatusCode.OK, one.StatusCode);
        PlayerPresenceDto presenceOne = (await one.Content.ReadFromJsonAsync<PlayerPresenceDto>())!;
        Assert.Equal(("Nym", One), (presenceOne.Target.Name, presenceOne.Instance.WorldId));
    }

    [Fact]
    public async Task Answer_404_for_a_world_the_caller_may_not_enter() =>
        Assert.Equal(HttpStatusCode.NotFound, (await Get($"/world/{Staff}/observability/character/7")).StatusCode);

    [Fact]
    public async Task Answer_503_for_an_unavailable_world() =>
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Get($"/world/{Down}/observability/character/7")).StatusCode);

    [Fact]
    public async Task Serve_no_per_character_presence_outside_a_world() =>
        Assert.Equal(HttpStatusCode.NotFound, (await Get("/observability/character/7")).StatusCode);

    [Fact]
    public async Task List_every_permitted_worlds_character_with_the_same_id_each_with_its_world()
    {
        HttpResponseMessage response = await Get("/observability/online");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        PagedResult<OnlinePlayerDto> page = (await response.Content.ReadFromJsonAsync<PagedResult<OnlinePlayerDto>>())!;
        Assert.Equal([(7u, One), (7u, Two), (7u, Down)],
            page.Items.Select(r => (r.CharacterId, r.WorldId)).OrderBy(r => r.WorldId));
    }
}
