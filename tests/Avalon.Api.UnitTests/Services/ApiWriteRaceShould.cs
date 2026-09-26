using System.Net;
using System.Text;
using Avalon.Api.Authentication;
using Avalon.Api.Authentication.Jwt;
using Avalon.Api.Config;
using Avalon.Api.Contract;
using Avalon.Api.Controllers;
using Avalon.Api.Services;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Services;
using Avalon.Server.Auth.UnitTests.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using DomainStatus = Avalon.Domain.Auth.AccountStatus;

namespace Avalon.Api.UnitTests.Services;

/// <summary>
/// #478: the API wrote back the whole account row it had read (login, MFA verify, password change,
/// email change, role change), so a ban or a lock that the Auth server or an admin wrote in
/// between was put back to the old values. Each write below has a ban and a lock land right after
/// its read, over the real repository and a real relational schema, and both must survive it, as
/// they do on the Auth server since #484.
/// </summary>
public sealed class ApiWriteRaceShould : IDisposable
{
    private const string Password = "correct horse";

    private readonly SqliteAuthDatabase _database = new();
    private readonly AccountRepository _accounts;
    private readonly IReplicatedCache _cache = Substitute.For<IReplicatedCache>();
    private DateTime _lockedUntil;

    public ApiWriteRaceShould()
    {
        _accounts = new AccountRepository(_database);
    }

    public void Dispose() => _database.Dispose();

    private async Task<Account> AccountAsync()
    {
        string salt = BCrypt.Net.BCrypt.GenerateSalt(4);
        return await _accounts.CreateAsync(new Account
        {
            Username = "RACEUSER",
            Email = "race@avalon.monster",
            Salt = Encoding.UTF8.GetBytes(salt),
            Verifier = Encoding.UTF8.GetBytes(BCrypt.Net.BCrypt.HashPassword(Password, salt)),
            JoinDate = DateTime.UtcNow,
            LastLogin = DateTime.UtcNow.AddDays(-1),
        });
    }

    /// <summary>What an admin ban and a lock by failed logins write, landing after the request's read.</summary>
    private async Task BanAndLockAsync(AccountId id)
    {
        _lockedUntil = DateTime.UtcNow.AddMinutes(15);
        await using AuthDbContext context = _database.CreateDbContext();
        await context.Accounts.Where(a => a.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(a => a.Status, DomainStatus.Banned)
            .SetProperty(a => a.Locked, true)
            .SetProperty(a => a.FailedLogins, 5)
            .SetProperty(a => a.LockedUntil, (DateTime?)_lockedUntil));
    }

    private async Task<Account> StoredAsync(AccountId id)
    {
        await using AuthDbContext context = _database.CreateDbContext();
        return await context.Accounts.AsNoTracking().SingleAsync(a => a.Id == id);
    }

    private async Task AssertBanAndLockSurvivedAsync(AccountId id)
    {
        Account stored = await StoredAsync(id);
        Assert.Equal(DomainStatus.Banned, stored.Status);
        Assert.True(stored.Locked);
        Assert.Equal(5, stored.FailedLogins);
        Assert.Equal(_lockedUntil, stored.LockedUntil!.Value, TimeSpan.FromMilliseconds(1));
    }

    private StaleAccountRepository Stale(Account account) =>
        new(_accounts) { AfterRead = () => BanAndLockAsync(account.Id) };

    private AccountService Service(IAccountRepository accounts) => new(NullLoggerFactory.Instance, accounts,
        Substitute.For<IJwtUtils>(), Substitute.For<IMFAHashService>(), new MfaSetupRepository(_database),
        new DeviceRepository(_database), _cache, Substitute.For<ISecureRandom>(), Substitute.For<IRefreshTokenService>(),
        new DbTransactionRunner<AuthDbContext>(_database), new AuthenticationConfig(),
        TestLogin.Password(accounts, _cache), TestLogin.Reauthentication(accounts, _cache));

    [Fact]
    public async Task Keep_a_ban_and_a_lock_written_while_a_login_was_being_recorded()
    {
        Account account = await AccountAsync();

        await Assert.ThrowsAnyAsync<Exception>(() => Service(Stale(account)).Authenticate(
            new AuthenticateRequest { Username = "raceuser", Password = Password }, IPAddress.Loopback,
            CancellationToken.None));

        await AssertBanAndLockSurvivedAsync(account.Id);
    }

    [Fact]
    public async Task Keep_a_ban_and_a_lock_written_while_a_password_was_being_changed()
    {
        Account account = await AccountAsync();

        await Service(Stale(account)).ChangePasswordAsync(account.Id, Password, "a new strong one", IPAddress.Loopback);

        await AssertBanAndLockSurvivedAsync(account.Id);
        Account stored = await StoredAsync(account.Id);
        Assert.True(BCrypt.Net.BCrypt.Verify("a new strong one", Encoding.UTF8.GetString(stored.Verifier)));
    }

    /// <summary>
    /// The email change was started (with the password) before the ban and the lock; the confirm
    /// writes the email by column in its own transaction (#503), reading no row to write back.
    /// </summary>
    [Fact]
    public async Task Keep_a_ban_and_a_lock_written_while_an_email_change_was_being_confirmed()
    {
        Account account = await AccountAsync();
        _cache.GetAsync("auth:emailChange:token").Returns($"{account.Id.Value}|0|new@avalon.monster");
        _cache.RemoveAsync("auth:emailChange:token").Returns(true);
        await BanAndLockAsync(account.Id);

        await Service(_accounts).ConfirmEmailChangeAsync("token");

        await AssertBanAndLockSurvivedAsync(account.Id);
        Assert.Equal("new@avalon.monster", (await StoredAsync(account.Id)).Email);
    }

    /// <summary>The role change writes the level by column in its own transaction (#504), reading no row to write back.</summary>
    [Fact]
    public async Task Keep_a_ban_and_a_lock_written_while_roles_were_being_changed()
    {
        Account account = await AccountAsync();
        await BanAndLockAsync(account.Id);

        await Service(_accounts).UpdateRolesAsync(account.Id, Contract.AccountAccessLevel.GameMaster, new AccountId(1));

        await AssertBanAndLockSurvivedAsync(account.Id);
        Assert.Equal(Avalon.Common.Accounts.AccountAccessLevel.GameMaster, (await StoredAsync(account.Id)).AccessLevel);
    }

    [Fact]
    public async Task Keep_a_ban_and_a_lock_written_while_an_mfa_verify_was_being_recorded()
    {
        Account account = await AccountAsync();
        StaleAccountRepository stale = Stale(account);
        IMFAService mfa = Substitute.For<IMFAService>();
        mfa.VerifyMFAAsync("hash", "123456", Arg.Any<CancellationToken>()).Returns(new MFAVerifyResult(true, account.Id));
        IMFAHashService hashes = Substitute.For<IMFAHashService>();
        hashes.GetAccountIdAsync("hash").Returns(account.Id);
        hashes.RecordAttemptAsync(account.Id).Returns(1L);
        IRefreshTokenService refresh = Substitute.For<IRefreshTokenService>();
        refresh.IssueAsync(Arg.Any<AccountId>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new RefreshIssueResult("raw", DateTime.UtcNow.AddDays(1), Guid.NewGuid()));
        var controller = new MFAController(mfa, Substitute.For<IAuthContext>(), new AuthenticationConfig(),
            Substitute.For<IJwtUtils>(), stale, refresh, TestLogin.Mfa(stale, _cache, mfa, hashes),
            Substitute.For<IReauthentication>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { Connection = { RemoteIpAddress = IPAddress.Loopback } } },
        };

        ActionResult<AuthenticateResponse> result = await controller.VerifyMFA(new VerifyMFARequest { Hash = "hash", Code = "123456" });

        await AssertBanAndLockSurvivedAsync(account.Id);
        Assert.Null(result.Value);
        await refresh.DidNotReceiveWithAnyArgs().IssueAsync(default!, default, default);
    }
}
