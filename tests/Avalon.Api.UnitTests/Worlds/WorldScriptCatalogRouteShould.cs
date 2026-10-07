using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Avalon.Api.Contract;
using Avalon.Api.Templates;
using Avalon.Api.UnitTests.Authentication;
using Avalon.Api.Worlds;
using Avalon.Database;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Scripts;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;
using AccountAccessLevel = Avalon.Common.Accounts.AccountAccessLevel;
using WorldEntity = Avalon.Domain.Auth.World;

namespace Avalon.Api.UnitTests.Worlds;

/// <summary>
/// The names a world offers for a template's script field: <c>GET /world/{id}/scripts</c>, for GameMasters, behind the
/// #523 world check, reading only that world's catalog key. A world that has not published one answers with empty
/// lists and <c>published: false</c>, so the admin app can tell "no scripts" from "no word yet".
/// </summary>
public sealed class WorldScriptCatalogRouteShould : IAsyncLifetime
{
    private const ushort One = 1;
    private const ushort Two = 2;

    // Not script names a world has: stand-ins for whatever a world publishes.
    private static readonly string[] OneAi = ["AiOne", "AiTwo"];
    private static readonly string[] OneAbility = ["AbilityOne"];
    private static readonly string[] OneQuest = ["QuestOne", "QuestTwo", "QuestThree"];
    private static readonly string[] OneItem = ["ItemOne"];
    private static readonly string[] OneAura = ["AuraOne"];

    private readonly IReplicatedCache _cache = Substitute.For<IReplicatedCache>();
    private readonly IWorldRepository _authWorlds = Substitute.For<IWorldRepository>();
    private ApiAuthHost _host = null!;

    public async Task InitializeAsync()
    {
        WorldDatabases databases = new(new[] { One, Two }.Select(id => new ConfiguredWorld(new WorldId(id), $"Host=w{id}", $"Host=c{id}")));
        Row(One);
        Row(Two);
        _cache.GetAsync(CacheKeys.WorldScriptCatalog(One))
            .Returns(ScriptCatalogJson.Serialize(new ScriptCatalogSnapshot(OneAi, OneAbility, OneQuest, OneItem, OneAura)));

        _host = await ApiAuthHost.StartAsync(_cache, services =>
        {
            services.AddWorldDatabases(databases);
            services.AddSingleton(_authWorlds);
            services.AddSingleton(Substitute.For<IWorldRepositories>());
            services.AddSingleton<IWorldScriptCatalog, WorldScriptCatalog>();
        });
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private void Row(ushort id) =>
        _authWorlds.FindByIdAsync(Arg.Is<WorldId>(w => w.Value == id), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new WorldEntity
            {
                Id = new WorldId(id),
                Name = $"World{id}",
                AccessLevelRequired = AccountAccessLevel.Player,
                Host = "h",
                MinVersion = "0.0.1",
                Version = "0.0.1",
            });

    private Task<HttpResponseMessage> Get(string path, AccountAccessLevel level)
    {
        Account account = ApiAuthHost.MakeAccount(level);
        _host.AccountNowIs(account);
        return _host.GetAsync(path, ApiAuthHost.Mint(account));
    }

    [Fact]
    public async Task Answer_a_game_master_with_the_published_catalog()
    {
        HttpResponseMessage response = await Get($"/world/{One}/scripts", AccountAccessLevel.GameMaster);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        WorldScriptCatalogDto dto = (await response.Content.ReadFromJsonAsync<WorldScriptCatalogDto>())!;
        Assert.True(dto.Published);
        Assert.Equal(OneAi, dto.Ai);
        Assert.Equal(OneAbility, dto.Ability);
        Assert.Equal(OneQuest, dto.Quest);
        Assert.Equal(OneItem, dto.Item);
        Assert.Equal(OneAura, dto.Aura);
    }

    [Fact]
    public async Task Spell_the_fields_in_camel_case()
    {
        HttpResponseMessage response = await Get($"/world/{One}/scripts", AccountAccessLevel.GameMaster);

        JsonObject body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        Assert.Equal(["ai", "ability", "quest", "item", "aura", "published"], body.Select(p => p.Key));
    }

    [Fact]
    public async Task Say_published_false_with_empty_lists_when_the_world_has_not_reported_in()
    {
        HttpResponseMessage response = await Get($"/world/{Two}/scripts", AccountAccessLevel.GameMaster);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        WorldScriptCatalogDto dto = (await response.Content.ReadFromJsonAsync<WorldScriptCatalogDto>())!;
        Assert.False(dto.Published);
        Assert.Empty(dto.Ai);
        Assert.Empty(dto.Ability);
        Assert.Empty(dto.Quest);
        Assert.Empty(dto.Item);
        Assert.Empty(dto.Aura);
    }

    [Fact]
    public async Task Say_published_false_when_the_stored_value_is_not_a_catalog()
    {
        _cache.GetAsync(CacheKeys.WorldScriptCatalog(Two)).Returns("not json");

        HttpResponseMessage response = await Get($"/world/{Two}/scripts", AccountAccessLevel.GameMaster);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await response.Content.ReadFromJsonAsync<WorldScriptCatalogDto>())!.Published);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Say_published_false_instead_of_failing_when_redis_cannot_be_read(bool wrongType)
    {
        Exception failure = wrongType
            ? new StackExchange.Redis.RedisServerException("WRONGTYPE")
            : new StackExchange.Redis.RedisTimeoutException("timeout", StackExchange.Redis.CommandStatus.Unknown);
        _cache.GetAsync(CacheKeys.WorldScriptCatalog(Two)).Returns(Task.FromException<string?>(failure));

        HttpResponseMessage response = await Get($"/world/{Two}/scripts", AccountAccessLevel.GameMaster);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await response.Content.ReadFromJsonAsync<WorldScriptCatalogDto>())!.Published);
    }

    [Fact]
    public async Task Refuse_a_player()
    {
        HttpResponseMessage response = await Get($"/world/{One}/scripts", AccountAccessLevel.Player);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Read_only_the_named_worlds_key()
    {
        await Get($"/world/{Two}/scripts", AccountAccessLevel.GameMaster);

        await _cache.DidNotReceive().GetAsync(CacheKeys.WorldScriptCatalog(One));
    }

    [Fact]
    public void Carry_the_operation_id_the_admin_app_generates_its_client_from()
    {
        string?[] names = _host.Endpoints
            .Select(e => e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.EndpointNameMetadata>()?.EndpointName)
            .ToArray();

        Assert.Contains("GetWorldScripts", names);
    }
}
