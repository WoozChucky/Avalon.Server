using System.Net;
using Avalon.Api.Config;
using Avalon.Api.Contract;
using Avalon.Api.Exceptions;
using Avalon.Api;
using Avalon.Api.Services;
using Avalon.Database;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Infrastructure.Services;
using Avalon.Server.Auth.UnitTests.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.Services;

/// <summary>
/// #495: <c>POST /account/register</c> answers whether a username or an email exists, and had no
/// limit, so it could be asked about as many names as a caller liked. Every registration now takes
/// a slot from its source's budget, the one the logins share, before any lookup. A registration
/// that creates an account gives its slot back; one answered "already exists" keeps it.
/// </summary>
public sealed class RegistrationThrottleShould : IDisposable
{
    private const string LoopbackSourceKey = "auth:source:127.0.0.1:failedLogins";

    private readonly SqliteAuthDatabase _database = new();
    private readonly CounterCache _counters = new();
    private readonly AuthenticationConfig _config = new() { MaxFailedLoginsPerSource = 3 };
    private readonly AccountService _service;

    public RegistrationThrottleShould()
    {
        AccountRepository accounts = new(_database);
        _service = new AccountService(
            NullLoggerFactory.Instance,
            accounts,
            Substitute.For<Avalon.Api.Authentication.Jwt.IJwtUtils>(),
            Substitute.For<IMFAHashService>(),
            new MfaSetupRepository(_database),
            new DeviceRepository(_database),
            _counters.Cache,
            Substitute.For<ISecureRandom>(),
            Substitute.For<IRefreshTokenService>(),
            Substitute.For<IDbTransactionRunner<AuthDbContext>>(),
            _config,
            TestLogin.Password(accounts, _counters.Cache, _config),
            TestLogin.Reauthentication(accounts, _counters.Cache, _config));
    }

    public void Dispose() => _database.Dispose();

    private Task Register(string username, string email, IPAddress? from = null) => _service.Register(
        new RegisterRequest { Username = username, Password = TestPasswords.Valid, Email = email },
        "test-agent", from ?? IPAddress.Loopback, CancellationToken.None);

    private async Task<int> AccountsAsync()
    {
        await using AuthDbContext context = _database.CreateDbContext();
        // The seeded ADMIN account is not counted.
        return await context.Accounts.CountAsync(a => a.Username != "ADMIN");
    }

    [Fact]
    public async Task Refuse_a_source_past_its_budget_before_saying_whether_a_username_exists()
    {
        await Register("taken", "taken@avalon.monster");
        for (int i = 0; i < 3; i++)
            await Assert.ThrowsAsync<BusinessException>(() => Register("taken", $"probe{i}@avalon.monster"));

        // Past the budget: LOCKED, the same 429 a login gets, and nothing about the name.
        await Assert.ThrowsAsync<AccountLockedException>(() => Register("taken", "probe@avalon.monster"));
        await Assert.ThrowsAsync<AccountLockedException>(() => Register("free", "free@avalon.monster"));
        Assert.Equal(1, await AccountsAsync());
    }

    [Fact]
    public async Task Keep_the_slot_of_a_registration_answered_already_exists()
    {
        await Register("taken", "taken@avalon.monster");

        await Assert.ThrowsAsync<BusinessException>(() => Register("taken", "other@avalon.monster"));
        await Assert.ThrowsAsync<BusinessException>(() => Register("other", "taken@avalon.monster"));

        Assert.Equal(2, _counters.CountOf(LoopbackSourceKey));
    }

    [Fact]
    public async Task Give_back_the_slot_of_a_registration_that_created_an_account()
    {
        await Register("first", "first@avalon.monster");
        await Register("second", "second@avalon.monster");

        Assert.Equal(0, _counters.CountOf(LoopbackSourceKey));
        Assert.Equal(2, await AccountsAsync());
    }

    [Fact]
    public async Task Share_the_budget_logins_spend()
    {
        for (int i = 0; i < 3; i++)
            await _counters.Cache.IncrementAsync(LoopbackSourceKey, TimeSpan.FromMinutes(15));

        await Assert.ThrowsAsync<AccountLockedException>(() => Register("fresh", "fresh@avalon.monster"));
        Assert.Equal(0, await AccountsAsync());
    }

    // ---------------- The account-creation cap (#495 review) ----------------

    private const string LoopbackCreationKey = "auth:source:127.0.0.1:accountsCreated";

    [Fact]
    public async Task Refuse_a_source_past_its_account_creation_cap_although_its_login_budget_has_room()
    {
        _config.MaxAccountsCreatedPerSource = 2;
        await Register("one", "one@avalon.monster");
        await Register("two", "two@avalon.monster");

        await Assert.ThrowsAsync<AccountLockedException>(() => Register("three", "three@avalon.monster"));

        Assert.Equal(2, await AccountsAsync());
        // The two successes gave their login slots back; the refused third keeps its own.
        Assert.Equal(1, _counters.CountOf(LoopbackSourceKey));
        Assert.Equal(2, _counters.CountOf(LoopbackCreationKey)); // but not their creation slots
    }

    [Fact]
    public async Task Not_spend_the_creation_cap_on_a_registration_answered_already_exists()
    {
        _config.MaxAccountsCreatedPerSource = 1;
        await Register("taken", "taken@avalon.monster");
        await Assert.ThrowsAsync<BusinessException>(() => Register("taken", "other@avalon.monster"));

        Assert.Equal(1, _counters.CountOf(LoopbackCreationKey));
    }

    [Theory]
    [InlineData(0, 60, "MaxAccountsCreatedPerSource")]
    [InlineData(5, 0, "AccountCreationWindowMinutes")]
    public void Stop_startup_on_a_creation_cap_below_one(int max, int window, string setting)
    {
        var config = new AuthenticationConfig { MaxAccountsCreatedPerSource = max, AccountCreationWindowMinutes = window };

        var refused = Assert.Throws<InvalidOperationException>(() => ServiceRegistration.ValidateAccountCreationCap(config));

        Assert.Contains(setting, refused.Message);
    }

    [Fact]
    public async Task Cap_each_source_on_its_own()
    {
        _config.MaxAccountsCreatedPerSource = 1;
        await Register("one", "one@avalon.monster");

        await Register("two", "two@avalon.monster", IPAddress.Parse("10.0.0.9"));

        Assert.Equal(2, await AccountsAsync());
    }

    [Fact]
    public async Task Count_each_source_on_its_own()
    {
        await Register("taken", "taken@avalon.monster");
        for (int i = 0; i < 3; i++)
            await Assert.ThrowsAsync<BusinessException>(() => Register("taken", $"probe{i}@avalon.monster"));

        await Register("other", "other@avalon.monster", IPAddress.Parse("10.0.0.9"));

        Assert.Equal(2, await AccountsAsync());
    }
}
