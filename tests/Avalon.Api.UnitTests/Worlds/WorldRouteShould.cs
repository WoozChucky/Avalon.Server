using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Avalon.Api.Services;
using Avalon.Api.UnitTests.Authentication;
using Avalon.Api.Worlds;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.World.Repositories;
using Avalon.Domain.Auth;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;
using AccountAccessLevel = Avalon.Common.Accounts.AccountAccessLevel;
using WorldEntity = Avalon.Domain.Auth.World;

namespace Avalon.Api.UnitTests.Worlds;

/// <summary>
/// /world/{worldId}/... (#523) over HTTP, through the real authentication, world middleware and
/// policies: 404 for a world this api does not serve or the caller may not enter, 503 for one whose
/// databases failed at startup, otherwise the endpoint's own role policy.
/// </summary>
public sealed class WorldRouteShould : IAsyncLifetime
{
    private const ushort Open = 1;        // configured, available, Player
    private const ushort Down = 2;        // configured, unavailable, Player
    private const ushort Staff = 3;       // configured, available, Admin only
    private const ushort StaffDown = 4;   // configured, unavailable, Admin only
    private const ushort Unlisted = 5;    // configured, available, no auth Worlds row
    private const ushort Nowhere = 7;     // neither configured nor an auth Worlds row
    private const ushort Unconfigured = 9; // an auth Worlds row, no configuration

    private readonly IWorldRepository _authWorlds = Substitute.For<IWorldRepository>();
    private readonly IItemTemplateRepository _items = Substitute.For<IItemTemplateRepository>();
    private readonly ICharacterService _characters = Substitute.For<ICharacterService>();
    private ApiAuthHost _host = null!;

    public async Task InitializeAsync()
    {
        WorldDatabases databases = new(new ushort[] { Open, Down, Staff, StaffDown, Unlisted }
            .Select(id => new ConfiguredWorld(new WorldId(id), $"Host=w{id}", $"Host=c{id}")));
        databases.MarkUnavailable(new WorldId(Down));
        databases.MarkUnavailable(new WorldId(StaffDown));

        Row(Open, AccountAccessLevel.Player);
        Row(Down, AccountAccessLevel.Player);
        Row(Staff, AccountAccessLevel.Admin);
        Row(StaffDown, AccountAccessLevel.Admin);
        Row(Unconfigured, AccountAccessLevel.Player);

        _items.FindByIdAsync(Arg.Any<ItemTemplateId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new ItemTemplate { Id = new ItemTemplateId(1), Name = "Lantern" });
        _characters.PaginateAsync(Arg.Any<Avalon.Api.Contract.CharacterPaginateFilters>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<Character>(1, 50, 0, new List<Character>()));

        _host = await ApiAuthHost.StartAsync(configure: services =>
        {
            services.AddWorldDatabases(databases);
            services.AddSingleton(_authWorlds);
            services.AddSingleton(_items);
            services.AddSingleton(_characters);
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

    private string TokenFor(AccountAccessLevel level)
    {
        Account account = ApiAuthHost.MakeAccount(level);
        _host.AccountNowIs(account);
        return ApiAuthHost.Mint(account);
    }

    private Task<HttpResponseMessage> Item(string world, AccountAccessLevel level) =>
        _host.GetAsync($"/world/{world}/item-template/1", TokenFor(level));

    [Fact]
    public async Task Serve_a_permitted_available_world()
    {
        HttpResponseMessage response = await Item("1", AccountAccessLevel.Player);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Lantern", (await response.Content.ReadFromJsonAsync<Avalon.Api.Contract.ItemTemplateDto>())!.Name);
    }

    [Fact]
    public async Task Answer_404_for_a_world_this_api_is_not_configured_for() =>
        Assert.Equal(HttpStatusCode.NotFound, (await Item($"{Unconfigured}", AccountAccessLevel.Admin)).StatusCode);

    [Theory]
    [InlineData("0")]
    [InlineData("01")]
    [InlineData("-1")]
    [InlineData("65536")]
    [InlineData("65537")]
    public async Task Answer_404_for_a_world_id_that_is_not_canonical(string world)
    {
        HttpResponseMessage response = await Item(world, AccountAccessLevel.Admin);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await _items.DidNotReceive().FindByIdAsync(Arg.Any<ItemTemplateId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Answer_404_for_a_configured_world_with_no_auth_row() =>
        Assert.Equal(HttpStatusCode.NotFound, (await Item($"{Unlisted}", AccountAccessLevel.Admin)).StatusCode);

    [Fact]
    public async Task Answer_503_for_an_unavailable_world()
    {
        HttpResponseMessage response = await Item($"{Down}", AccountAccessLevel.Player);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("World 2 is unavailable", (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail);
    }

    [Fact]
    public async Task Answer_404_when_the_caller_may_not_enter_the_world() =>
        Assert.Equal(HttpStatusCode.NotFound, (await Item($"{Staff}", AccountAccessLevel.Player)).StatusCode);

    [Fact]
    public async Task Answer_404_not_503_when_the_caller_may_not_enter_an_unavailable_world() =>
        Assert.Equal(HttpStatusCode.NotFound, (await Item($"{StaffDown}", AccountAccessLevel.Player)).StatusCode);

    [Fact]
    public async Task Answer_the_same_404_for_a_restricted_world_as_for_an_unknown_one()
    {
        HttpResponseMessage restricted = await Item($"{Staff}", AccountAccessLevel.Player);
        HttpResponseMessage unknown = await Item($"{Unconfigured}", AccountAccessLevel.Player);

        Assert.Equal(HttpStatusCode.NotFound, restricted.StatusCode);
        Assert.Equal(unknown.StatusCode, restricted.StatusCode);
        Assert.Equal(await unknown.Content.ReadAsStringAsync(), await restricted.Content.ReadAsStringAsync());
        Assert.Equal(unknown.Content.Headers.ContentType?.MediaType, restricted.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData(Unconfigured)]
    [InlineData(Nowhere)]
    [InlineData(Staff)]
    public async Task Look_up_the_auth_row_before_asking_whether_the_world_is_configured(ushort world)
    {
        // An unconfigured world takes the same path as a restricted one, auth row lookup included,
        // so the time to its 404 does not tell the two apart.
        HttpResponseMessage response = await Item($"{world}", AccountAccessLevel.Player);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await _authWorlds.Received(1).FindByIdAsync(Arg.Is<WorldId>(w => w.Value == world), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Let_the_admitted_staff_in() =>
        Assert.Equal(HttpStatusCode.OK, (await Item($"{Staff}", AccountAccessLevel.Admin)).StatusCode);

    [Fact]
    public async Task Leave_the_role_policy_to_decide_otherwise()
    {
        HttpResponseMessage player = await _host.GetAsync("/world/1/character/paginate", TokenFor(AccountAccessLevel.Player));
        HttpResponseMessage gameMaster = await _host.GetAsync("/world/1/character/paginate", TokenFor(AccountAccessLevel.GameMaster));

        Assert.Equal(HttpStatusCode.Forbidden, player.StatusCode);
        Assert.Equal(HttpStatusCode.OK, gameMaster.StatusCode);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("9")]
    [InlineData("3")]
    public async Task Answer_401_to_an_anonymous_caller_whatever_the_world(string world)
    {
        HttpResponseMessage response = await _host.Client.GetAsync($"/world/{world}/item-template/1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Apply_the_world_rule_to_a_personal_access_token()
    {
        const string token = "avp_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        _host.Pats.FindByRawTokenAsync(token, Arg.Any<CancellationToken>()).Returns(new PersonalAccessToken
        {
            Id = new PersonalAccessTokenId(5),
            AccountId = new AccountId(ApiAuthHost.AccountIdValue),
            TokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(token)),
            Name = "ci",
            TokenPrefix = token[..8],
            Roles = AccountAccessLevel.Player,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(1),
        });
        _host.AccountNowIs(ApiAuthHost.MakeAccount(AccountAccessLevel.Player));

        async Task<HttpStatusCode> WithPat(ushort world)
        {
            using HttpRequestMessage request = new(HttpMethod.Get, $"/world/{world}/item-template/1");
            request.Headers.Authorization = new AuthenticationHeaderValue("Avalon", token);
            return (await _host.Client.SendAsync(request)).StatusCode;
        }

        Assert.Equal(HttpStatusCode.OK, await WithPat(Open));
        Assert.Equal(HttpStatusCode.NotFound, await WithPat(Staff));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, await WithPat(Down));
    }

    [Fact]
    public async Task Answer_503_when_the_worlds_database_fails_mid_request()
    {
        _items.FindByIdAsync(Arg.Any<ItemTemplateId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new NpgsqlException("down"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Item("1", AccountAccessLevel.Player)).StatusCode);
    }
}
