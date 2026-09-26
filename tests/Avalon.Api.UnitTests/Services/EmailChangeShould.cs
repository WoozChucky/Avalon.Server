using System.Net;
using System.Security.Authentication;
using System.Text;
using Avalon.Api.Authentication.Jwt;
using Avalon.Api.Config;
using Avalon.Api.Exceptions;
using Avalon.Api.Services;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Avalon.Infrastructure.Login;
using Avalon.Server.Auth.UnitTests.Services;
using NSubstitute;
using StackExchange.Redis;
using Xunit;
using AccountAccessLevel = Avalon.Common.Accounts.AccountAccessLevel;

namespace Avalon.Api.UnitTests.Services;

/// <summary>
/// #503: starting an email change needed only a session, and confirming it revoked the refresh
/// tokens but not the personal access tokens, and left the credentials version where it was. A
/// stolen session could therefore take the account's email, and a token minted with it survived.
/// Starting a change now needs the current password, and confirming it is a credentials change.
/// Real repositories over a real relational schema; the token store is a dictionary.
/// </summary>
public sealed class EmailChangeShould : IDisposable
{
    private readonly SqliteAuthDatabase _database = new();
    private readonly AccountRepository _accounts;
    private readonly IReplicatedCache _cache = Substitute.For<IReplicatedCache>();
    private readonly Dictionary<string, string> _store = new(StringComparer.Ordinal);

    public EmailChangeShould()
    {
        _accounts = new AccountRepository(_database);
        _cache.SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>()).Returns(call =>
        {
            _store[call.ArgAt<string>(0)] = call.ArgAt<string>(1);
            return true;
        });
        _cache.GetAsync(Arg.Any<string>()).Returns(call =>
            _store.TryGetValue(call.Arg<string>(), out string? value) ? value : null);
        _cache.RemoveAsync(Arg.Any<string>()).Returns(call => _store.Remove(call.Arg<string>()));
    }

    public void Dispose() => _database.Dispose();

    private async Task<Account> AccountAsync(string username = "OWNER", string? email = null)
    {
        string salt = BCrypt.Net.BCrypt.GenerateSalt(4);
        return await _accounts.CreateAsync(new Account
        {
            Username = username,
            Email = email ?? $"{username.ToLowerInvariant()}@avalon.monster",
            Salt = Encoding.UTF8.GetBytes(salt),
            Verifier = Encoding.UTF8.GetBytes(BCrypt.Net.BCrypt.HashPassword(TestPasswords.Valid, salt)),
            JoinDate = DateTime.UtcNow,
            LastLogin = DateTime.UtcNow,
        });
    }

    private async Task<Account> StoredAsync(AccountId id) => (await _accounts.FindByIdAsync(id))!;

    private PersonalAccessTokenService Pats() =>
        new(new PersonalAccessTokenRepository(_database), new SecureRandom(), TimeProvider.System);

    private AccountService Service(IReplicatedCache? cache = null) => new(NullLoggerFactory.Instance, _accounts,
        Substitute.For<IJwtUtils>(), Substitute.For<IMFAHashService>(), new MfaSetupRepository(_database),
        new DeviceRepository(_database), cache ?? _cache, new SecureRandom(),
        new DbTransactionRunner<AuthDbContext>(_database), new AuthenticationConfig(),
        TestLogin.Password(_accounts, cache ?? _cache), TestLogin.Reauthentication(_accounts, cache ?? _cache));

    private Task<string> StartAsync(AccountId id, string newEmail, string? password = null) =>
        Service().InitiateEmailChangeAsync(id, newEmail, password ?? TestPasswords.Valid, IPAddress.Loopback);

    private async Task ChangeAsync(AccountId id, string newEmail) =>
        await Service().ConfirmEmailChangeAsync(await StartAsync(id, newEmail));

    // ---------------- Starting a change needs the current password ----------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Refuse_to_start_a_change_without_the_current_password(string password)
    {
        Account account = await AccountAsync();

        AuthenticationException refused = await Assert.ThrowsAsync<AuthenticationException>(() =>
            StartAsync(account.Id, "new@avalon.monster", password));

        Assert.Equal(Reauthentication.InvalidPassword, refused.Message);
        Assert.Empty(_store);
    }

    [Fact]
    public async Task Refuse_to_start_a_change_with_a_wrong_password_and_count_it_as_a_failed_login()
    {
        Account account = await AccountAsync();

        await Assert.ThrowsAsync<AuthenticationException>(() =>
            StartAsync(account.Id, "new@avalon.monster", TestPasswords.Other));

        Assert.Empty(_store);
        Assert.Equal(1, (await StoredAsync(account.Id)).FailedLogins);
    }

    // ---------------- Confirming a change is a credentials change ----------------

    [Fact]
    public async Task Refuse_a_personal_access_token_minted_before_the_change()
    {
        Account account = await AccountAsync();
        MintResult minted = await Pats().MintSelfAsync(account.Id, AccountAccessLevel.Player, "cli", null, null,
            new Reauthenticated(account.Id, account.CredentialsVersion));

        await ChangeAsync(account.Id, "new@avalon.monster");

        PersonalAccessToken? stored = await Pats().FindByRawTokenAsync(minted.Token);
        Assert.NotNull(stored!.RevokedAt);
    }

    [Fact]
    public async Task Raise_the_version_revoke_the_refresh_tokens_and_publish_the_disconnect()
    {
        Account account = await AccountAsync();
        await new RefreshTokenService(new RefreshTokenRepository(_database), new SecureRandom(), TimeProvider.System)
            .IssueAsync(account.Id, account.CredentialsVersion);

        await ChangeAsync(account.Id, "new@avalon.monster");

        Account stored = await StoredAsync(account.Id);
        Assert.Equal("new@avalon.monster", stored.Email);
        Assert.Equal(account.CredentialsVersion + 1, stored.CredentialsVersion);
        await using AuthDbContext context = _database.CreateDbContext();
        Assert.True(await context.RefreshTokens.Where(t => t.AccountId == account.Id).AllAsync(t => t.Revoked));
        await _cache.Received(1).PublishAsync(CacheKeys.WorldAccountsDisconnectChannel,
            account.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>A login past its password step holds an MFA hash made at the old version; the change clears it.</summary>
    [Fact]
    public async Task Clear_the_pending_mfa_login_state()
    {
        Account account = await AccountAsync();
        string token = await StartAsync(account.Id, "new@avalon.monster");
        IDatabase redis = Substitute.For<IDatabase>();
        _cache.Database.Returns(redis);
        redis.HashGetAsync((RedisKey)CacheKeys.AccountMfa(account.Id.Value), (RedisValue)"hash", Arg.Any<CommandFlags>())
            .Returns((RedisValue)"PENDINGHASH");

        await Service().ConfirmEmailChangeAsync(token);

        await _cache.Received(1).RemoveAsync(CacheKeys.MfaReverseHash("PENDINGHASH"));
        await _cache.Received(1).RemoveAsync(CacheKeys.AccountMfa(account.Id.Value));
    }

    // ---------------- "Email already exists" costs a slot (review, #503) ----------------

    private static readonly string SourceKey = SourceBudget.KeyFor(IPAddress.Loopback);

    /// <summary>
    /// The start answers whether an address is taken, once the password is proved. The proof gives
    /// back its own slots, so without a slot of its own the answer was free: a session holding the
    /// password could test addresses without limit. It now keeps a slot from the source's budget,
    /// as registration does, and gives it back only when the change is started.
    /// </summary>
    [Fact]
    public async Task Keep_a_source_slot_when_the_address_is_taken()
    {
        await AccountAsync("OTHER", "taken@avalon.monster");
        Account account = await AccountAsync();
        var counters = new CounterCache();

        await Assert.ThrowsAsync<BusinessException>(() => Service(counters.Cache)
            .InitiateEmailChangeAsync(account.Id, "taken@avalon.monster", TestPasswords.Valid, IPAddress.Loopback));

        Assert.Equal(1, counters.CountOf(SourceKey));
    }

    [Fact]
    public async Task Give_the_source_slot_back_when_the_change_is_started()
    {
        Account account = await AccountAsync();
        var counters = new CounterCache();

        await Service(counters.Cache)
            .InitiateEmailChangeAsync(account.Id, "free@avalon.monster", TestPasswords.Valid, IPAddress.Loopback);

        Assert.Equal(0, counters.CountOf(SourceKey));
    }

    [Fact]
    public async Task Refuse_as_locked_once_taken_addresses_have_spent_the_source_budget()
    {
        await AccountAsync("OTHER", "taken@avalon.monster");
        Account account = await AccountAsync();
        var counters = new CounterCache();
        AccountService service = Service(counters.Cache);
        int budget = new AuthenticationConfig().MaxFailedLoginsPerSource;

        for (int i = 0; i < budget; i++)
            await Assert.ThrowsAsync<BusinessException>(() =>
                service.InitiateEmailChangeAsync(account.Id, "taken@avalon.monster", TestPasswords.Valid, IPAddress.Loopback));

        await Assert.ThrowsAsync<AccountLockedException>(() =>
            service.InitiateEmailChangeAsync(account.Id, "taken@avalon.monster", TestPasswords.Valid, IPAddress.Loopback));
    }

    /// <summary>
    /// The change was started with the password; the owner then changed the password before the
    /// confirm. The pending change was made on the strength of the old credentials and is void.
    /// </summary>
    [Fact]
    public async Task Refuse_a_confirm_whose_start_proved_credentials_that_have_since_changed()
    {
        Account account = await AccountAsync();
        string token = await StartAsync(account.Id, "thief@avalon.monster");
        await Service().ChangePasswordAsync(account.Id, TestPasswords.Valid, TestPasswords.Other, IPAddress.Loopback);

        await Assert.ThrowsAsync<BusinessException>(() => Service().ConfirmEmailChangeAsync(token));

        Assert.Equal("owner@avalon.monster", (await StoredAsync(account.Id)).Email);
    }

    // ---------------- Normalised and unique ----------------

    [Fact]
    public async Task Store_the_new_email_trimmed_and_lower_cased()
    {
        Account account = await AccountAsync();

        await ChangeAsync(account.Id, "  New.Owner@Avalon.MONSTER ");

        Assert.Equal("new.owner@avalon.monster", (await StoredAsync(account.Id)).Email);
    }

    [Fact]
    public async Task Refuse_to_start_a_change_to_an_email_another_account_holds_in_another_case()
    {
        await AccountAsync("OTHER", "taken@avalon.monster");
        Account account = await AccountAsync();

        BusinessException refused = await Assert.ThrowsAsync<BusinessException>(() =>
            StartAsync(account.Id, "Taken@Avalon.Monster"));

        Assert.Equal("Email already exists", refused.Message);
        Assert.Empty(_store);
    }

    /// <summary>Two changes to one address, both started while it was free: the second confirm loses to the index.</summary>
    [Fact]
    public async Task Refuse_the_second_confirm_of_two_changes_to_one_address()
    {
        Account first = await AccountAsync("FIRST");
        Account second = await AccountAsync("SECOND");
        string firstToken = await StartAsync(first.Id, "shared@avalon.monster");
        string secondToken = await StartAsync(second.Id, "SHARED@avalon.monster");

        await Service().ConfirmEmailChangeAsync(firstToken);
        BusinessException refused = await Assert.ThrowsAsync<BusinessException>(() =>
            Service().ConfirmEmailChangeAsync(secondToken));

        Assert.Equal("Email already exists", refused.Message);
        Assert.Equal("second@avalon.monster", (await StoredAsync(second.Id)).Email);
        Assert.Equal(second.CredentialsVersion, (await StoredAsync(second.Id)).CredentialsVersion);
    }

    [Theory]
    [InlineData("x")]
    [InlineData("player@ex\u00E4mple.com")]
    public async Task Refuse_to_start_a_change_to_an_email_that_is_not_an_ascii_address(string email)
    {
        Account account = await AccountAsync();

        BusinessException refused = await Assert.ThrowsAsync<BusinessException>(() => StartAsync(account.Id, email));

        Assert.Equal(AccountEmail.Requirement, refused.Message);
        Assert.Empty(_store);
    }

    [Fact]
    public async Task Find_an_account_by_its_email_in_any_case()
    {
        Account account = await AccountAsync();

        Account? found = await _accounts.FindByEmailAsync(" OWNER@Avalon.Monster");

        Assert.Equal(account.Id, found?.Id);
    }

    [Theory]
    [InlineData("Upper@avalon.monster")]
    [InlineData(" padded@avalon.monster")]
    [InlineData("padded@avalon.monster ")]
    public async Task Refuse_a_row_whose_email_is_not_stored_normalised(string email)
    {
        await Assert.ThrowsAsync<DbUpdateException>(() => AccountAsync("RAW", email));
    }

    [Fact]
    public async Task Refuse_a_second_row_with_the_same_email()
    {
        await AccountAsync("FIRST", "same@avalon.monster");

        await Assert.ThrowsAsync<DbUpdateException>(() => AccountAsync("SECOND", "same@avalon.monster"));
    }
}
