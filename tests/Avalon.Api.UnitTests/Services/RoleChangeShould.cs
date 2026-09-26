using System.Net;
using System.Text;
using Avalon.Api.Authentication.Jwt;
using Avalon.Api.Config;
using Avalon.Api.Exceptions;
using Avalon.Api.Services;
using Avalon.Api.UnitTests.Authentication;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StackExchange.Redis;
using Xunit;
using AccountAccessLevel = Avalon.Common.Accounts.AccountAccessLevel;
using ContractLevel = Avalon.Api.Contract.AccountAccessLevel;

namespace Avalon.Api.UnitTests.Services;

/// <summary>
/// #504: a role change left every live session of the account as it was. A JWT kept its roles
/// until the next request, a refresh token still rotated, a personal access token still worked,
/// and a logged-in game-client connection kept its old access until world select. A role change is
/// now a credentials change: the version is raised and every refresh token and personal access
/// token revoked in its transaction, and the account is published on the disconnect channel, for a
/// promotion as for a demotion. Real repositories over a real relational schema.
/// </summary>
public sealed class RoleChangeShould : IDisposable
{
    private readonly SqliteAuthDatabase _database = new();
    private readonly AccountRepository _accounts;
    private readonly IReplicatedCache _cache = Substitute.For<IReplicatedCache>();
    private static readonly AccountId Admin = new(1);

    public RoleChangeShould()
    {
        _accounts = new AccountRepository(_database);
    }

    public void Dispose() => _database.Dispose();

    /// <summary>The account <see cref="ApiAuthHost"/> authenticates, id 7, held by the real database.</summary>
    private async Task<Account> AccountAsync(AccountAccessLevel level)
    {
        string salt = BCrypt.Net.BCrypt.GenerateSalt(4);
        return await _accounts.CreateAsync(new Account
        {
            Id = new AccountId(ApiAuthHost.AccountIdValue),
            Username = "STAFFER",
            Email = "staffer@avalon.monster",
            Salt = Encoding.UTF8.GetBytes(salt),
            Verifier = Encoding.UTF8.GetBytes(BCrypt.Net.BCrypt.HashPassword(TestPasswords.Valid, salt)),
            JoinDate = DateTime.UtcNow,
            LastLogin = DateTime.UtcNow,
            AccessLevel = level,
        });
    }

    private async Task<Account> StoredAsync(AccountId id) => (await _accounts.FindByIdAsync(id))!;

    private RefreshTokenService Refresh() =>
        new(new RefreshTokenRepository(_database), new SecureRandom(), TimeProvider.System);

    private PersonalAccessTokenService Pats() =>
        new(new PersonalAccessTokenRepository(_database), new SecureRandom(), TimeProvider.System);

    private AccountService Service() => new(NullLoggerFactory.Instance, _accounts, Substitute.For<IJwtUtils>(),
        Substitute.For<IMFAHashService>(), new MfaSetupRepository(_database), new DeviceRepository(_database), _cache,
        Substitute.For<ISecureRandom>(), Refresh(), new DbTransactionRunner<AuthDbContext>(_database),
        new AuthenticationConfig(), TestLogin.Password(_accounts, _cache), TestLogin.Reauthentication(_accounts, _cache));

    private Task DemoteAsync(AccountId id) => Service().UpdateRolesAsync(id, ContractLevel.Player, Admin);

    private const AccountAccessLevel GameMaster = AccountAccessLevel.Player | AccountAccessLevel.GameMaster;

    [Fact]
    public async Task Refuse_the_old_access_token_after_a_demotion()
    {
        Account account = await AccountAsync(GameMaster);
        string token = ApiAuthHost.Mint(account);
        await using ApiAuthHost host = await ApiAuthHost.StartAsync();

        await DemoteAsync(account.Id);
        host.AccountNowIs(await StoredAsync(account.Id));

        using HttpResponseMessage response = await host.GetAsync("/player", token);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refuse_to_rotate_the_old_refresh_token_after_a_demotion()
    {
        Account account = await AccountAsync(GameMaster);
        RefreshIssueResult issued = await Refresh().IssueAsync(account.Id, account.CredentialsVersion);

        await DemoteAsync(account.Id);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            Refresh().RotateAsync(issued.RawToken, RefreshCaller.From(IPAddress.Loopback, "test-agent")));
        await using AuthDbContext context = _database.CreateDbContext();
        Assert.True(await context.RefreshTokens.Where(t => t.AccountId == account.Id).AllAsync(t => t.Revoked));
        Assert.Equal(1, await context.RefreshTokens.CountAsync(t => t.AccountId == account.Id));
    }

    [Fact]
    public async Task Revoke_the_personal_access_tokens_on_a_demotion_in_the_admins_name()
    {
        Account account = await AccountAsync(GameMaster);
        MintResult minted = await Pats().MintSelfAsync(account.Id, GameMaster, "cli", null, null,
            new Reauthenticated(account.Id, account.CredentialsVersion));

        await DemoteAsync(account.Id);

        PersonalAccessToken? stored = await Pats().FindByRawTokenAsync(minted.Token);
        Assert.NotNull(stored!.RevokedAt);
        Assert.Equal(Admin, stored.RevokedBy);
    }

    [Theory]
    [InlineData(ContractLevel.Player)]
    [InlineData(ContractLevel.Player | ContractLevel.GameMaster | ContractLevel.Admin)]
    public async Task Raise_the_version_and_publish_the_disconnect_on_a_demotion_and_a_promotion(ContractLevel roles)
    {
        Account account = await AccountAsync(GameMaster);

        await Service().UpdateRolesAsync(account.Id, roles, Admin);

        Account stored = await StoredAsync(account.Id);
        Assert.Equal((AccountAccessLevel)roles, stored.AccessLevel);
        Assert.Equal(account.CredentialsVersion + 1, stored.CredentialsVersion);
        // The bare account id: what the World server and the Auth server both parse.
        await _cache.Received(1).PublishAsync(CacheKeys.WorldAccountsDisconnectChannel,
            account.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Change_the_roles_even_when_the_disconnect_publish_fails()
    {
        Account account = await AccountAsync(GameMaster);
        _cache.PublishAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns<Task>(_ => throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));

        await DemoteAsync(account.Id);

        Account stored = await StoredAsync(account.Id);
        Assert.Equal(AccountAccessLevel.Player, stored.AccessLevel);
        Assert.Equal(1, stored.CredentialsVersion);
    }

    [Fact]
    public async Task Answer_not_found_and_publish_nothing_for_an_account_that_does_not_exist()
    {
        await Assert.ThrowsAsync<BusinessException>(() => DemoteAsync(new AccountId(999)));

        await _cache.DidNotReceiveWithAnyArgs().PublishAsync(default!, default!);
    }
}
