using System.Net;
using System.Security.Authentication;
using System.Text;
using Avalon.Api.Config;
using Avalon.Api.Exceptions;
using Avalon.Api.Services;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Server.Auth.UnitTests.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Avalon.Api.UnitTests.Services;

/// <summary>
/// The current-password check in front of the password change, MFA setup and token minting
/// (#478, #483). A wrong password there is a failed login in every respect, or a stolen session
/// would be a password oracle with no limit.
/// </summary>
public sealed class ReauthenticationShould : IDisposable
{
    private static readonly string Password = TestPasswords.Valid;
    private const string SourceKey = "auth:source:127.0.0.1:failedLogins";

    private readonly SqliteAuthDatabase _database = new();
    private readonly AccountRepository _accounts;
    private readonly CounterCache _cache = new();
    private readonly AuthenticationConfig _config = new();
    private readonly Reauthentication _reauthentication;

    public ReauthenticationShould()
    {
        _accounts = new AccountRepository(_database);
        _reauthentication = TestLogin.Reauthentication(_accounts, _cache.Cache, _config);
    }

    public void Dispose() => _database.Dispose();

    private async Task<Account> AccountAsync(bool locked = false)
    {
        Account account = await _accounts.CreateAsync(new Account
        {
            Username = "CALLER",
            Email = "caller@avalon.monster",
            Salt = [1],
            Verifier = Encoding.UTF8.GetBytes(BCrypt.Net.BCrypt.HashPassword(Password, BCrypt.Net.BCrypt.GenerateSalt(4))),
            JoinDate = DateTime.UtcNow,
            LastLogin = DateTime.UtcNow,
            Locked = locked,
            LockedUntil = locked ? DateTime.UtcNow.AddMinutes(10) : null,
        });
        return account;
    }

    private Task CheckAsync(Account account, string password) =>
        _reauthentication.RequireCurrentPasswordAsync(account.Id, password, IPAddress.Loopback);

    private async Task<Account> StoredAsync(AccountId id)
    {
        await using AuthDbContext context = _database.CreateDbContext();
        return await context.Accounts.AsNoTracking().SingleAsync(a => a.Id == id);
    }

    [Fact]
    public async Task Refuse_an_empty_password_without_spending_anything()
    {
        Account account = await AccountAsync();

        var refused = await Assert.ThrowsAsync<AuthenticationException>(() => CheckAsync(account, " "));

        Assert.Equal(Reauthentication.InvalidPassword, refused.Message);
        Assert.Empty(_cache.UsernameKeys);
    }

    [Fact]
    public async Task Count_a_wrong_password_as_a_failed_login()
    {
        Account account = await AccountAsync();

        await Assert.ThrowsAsync<AuthenticationException>(() => CheckAsync(account, TestPasswords.Wrong));

        Assert.Equal(1, (await StoredAsync(account.Id)).FailedLogins);
        Assert.Equal(1, _cache.CountOf(Assert.Single(_cache.UsernameKeys)));
        Assert.Equal(1, _cache.CountOf(SourceKey));
    }

    [Fact]
    public async Task Lock_the_account_on_the_wrong_password_in_the_last_slot()
    {
        Account account = await AccountAsync();
        for (var i = 1; i < _config.MaxFailedLoginAttempts; i++)
            await Assert.ThrowsAsync<AuthenticationException>(() => CheckAsync(account, TestPasswords.Wrong));

        await Assert.ThrowsAsync<AccountLockedException>(() => CheckAsync(account, TestPasswords.Wrong));

        Assert.True((await StoredAsync(account.Id)).Locked);
        await Assert.ThrowsAsync<AccountLockedException>(() => CheckAsync(account, Password));
    }

    [Fact]
    public async Task Refuse_a_locked_account_even_with_the_right_password()
    {
        Account account = await AccountAsync(locked: true);

        await Assert.ThrowsAsync<AccountLockedException>(() => CheckAsync(account, Password));
    }

    /// <summary>Proved, but no login completed: only its own slots come back, earlier failures stay.</summary>
    [Fact]
    public async Task Give_back_only_its_own_slots_for_the_right_password()
    {
        Account account = await AccountAsync();
        await Assert.ThrowsAsync<AuthenticationException>(() => CheckAsync(account, TestPasswords.Wrong));

        await CheckAsync(account, Password);

        Assert.Equal(1, _cache.CountOf(Assert.Single(_cache.UsernameKeys)));
        Assert.Equal(1, _cache.CountOf(SourceKey));
    }
}
