using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using Avalon.Api.Authentication.Jwt;
using Avalon.Api.Config;
using Avalon.Api.Contract;
using Avalon.Api.Exceptions;
using Avalon.Api.Services;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Login;
using Avalon.Infrastructure.Services;
using Avalon.Server.Auth.UnitTests.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.Services;

/// <summary>
/// #478: the REST password login ignored the account lock, counted no failure and had no budget,
/// so it was a way around the lockout that protects the game client's login. It now runs the
/// login policy the Auth server runs, over the same Redis keys and the same SQL writes. These run
/// the real service over a real relational schema and a cache whose counters behave like the
/// Redis scripts.
/// </summary>
public sealed class RestLoginPolicyShould : IDisposable
{
    private const string Password = "correct horse";

    private readonly SqliteAuthDatabase _database = new();
    private readonly AccountRepository _accounts;
    private readonly CounterCache _cache = new();
    private readonly CountingVerifier _verifier = new();
    private readonly AuthenticationConfig _config = new();

    public RestLoginPolicyShould()
    {
        _accounts = new AccountRepository(_database);
    }

    public void Dispose() => _database.Dispose();

    private AccountService Service(IAccountRepository? accounts = null)
    {
        IAccountRepository repository = accounts ?? _accounts;
        IJwtUtils jwt = Substitute.For<IJwtUtils>();
        jwt.GenerateJwtToken(Arg.Any<Account>()).Returns("jwt");
        return new AccountService(NullLoggerFactory.Instance, repository, jwt, Substitute.For<IMFAHashService>(),
            new MfaSetupRepository(_database), new DeviceRepository(_database), _cache.Cache,
            Substitute.For<ISecureRandom>(), Substitute.For<IRefreshTokenService>(),
            new DbTransactionRunner<AuthDbContext>(_database), _config,
            TestLogin.Password(repository, _cache.Cache, _config, _verifier),
            TestLogin.Reauthentication(repository, _cache.Cache, _config, _verifier));
    }

    private static Account NewAccount(string username = "CALLER") => new()
    {
        Username = username,
        Email = $"{username.ToLowerInvariant()}@avalon.monster",
        Salt = [1],
        Verifier = Encoding.UTF8.GetBytes(BCrypt.Net.BCrypt.HashPassword(Password, BCrypt.Net.BCrypt.GenerateSalt(4))),
        JoinDate = DateTime.UtcNow,
        LastLogin = DateTime.UtcNow.AddDays(-1),
    };

    private Task<(AuthenticateResponse Response, AccountId? AccountId)> LoginAsync(string password,
        string username = "caller", AccountService? service = null) =>
        (service ?? Service()).Authenticate(new AuthenticateRequest { Username = username, Password = password },
            IPAddress.Loopback, CancellationToken.None);

    private async Task<Account> StoredAsync(AccountId id)
    {
        await using AuthDbContext context = _database.CreateDbContext();
        return await context.Accounts.AsNoTracking().SingleAsync(a => a.Id == id);
    }

    /// <summary>The key format both servers use, computed here without the code under test.</summary>
    private static string UsernameKey(string username) =>
        $"auth:username:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(username.Trim().ToUpperInvariant())))}:failedLogins";

    private const string LoopbackSourceKey = "auth:source:127.0.0.1:failedLogins";

    [Fact]
    public async Task Refuse_a_locked_account_before_checking_its_password()
    {
        Account account = await _accounts.CreateAsync(NewAccount());
        await using (AuthDbContext context = _database.CreateDbContext())
        {
            await context.Accounts.Where(a => a.Id == account.Id).ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Locked, true)
                .SetProperty(a => a.LockedUntil, DateTime.UtcNow.AddMinutes(10)));
        }

        var refused = await Assert.ThrowsAsync<AccountLockedException>(() => LoginAsync(Password));

        Assert.Equal("LOCKED", refused.Message);
        // One verify, against the fixed hash (#478 review), as an unknown username pays; the
        // account's own hash is never checked.
        Assert.Equal(new[] { BCryptPasswordVerifier.UnknownAccountHash }, _verifier.Hashes);
    }

    [Fact]
    public async Task Lock_the_account_after_the_limit_of_failed_logins()
    {
        Account account = await _accounts.CreateAsync(NewAccount());

        for (var i = 1; i < _config.MaxFailedLoginAttempts; i++)
            await Assert.ThrowsAsync<AuthenticationException>(() => LoginAsync("wrong"));
        // The failure in the last slot is the one that locks, and is answered as locked.
        await Assert.ThrowsAsync<AccountLockedException>(() => LoginAsync("wrong"));

        Account stored = await StoredAsync(account.Id);
        Assert.True(stored.Locked);
        Assert.Equal(_config.MaxFailedLoginAttempts, stored.FailedLogins);
        Assert.NotNull(stored.LockedUntil);

        // Now the right password is refused too, without being checked.
        int verifies = _verifier.Count;
        await Assert.ThrowsAsync<AccountLockedException>(() => LoginAsync(Password));
        Assert.Equal(verifies, _verifier.Count);
    }

    /// <summary>
    /// Three wrong passwords at the game client's login (the policy CAuthHandler runs, on the same
    /// cache and database) and two over REST lock the account: one count, one threshold, whichever
    /// server a guess goes through.
    /// </summary>
    [Fact]
    public async Task Share_the_failure_count_and_its_keys_with_the_game_client_login()
    {
        Account account = await _accounts.CreateAsync(NewAccount());
        PasswordLoginPolicy tcp = TestLogin.Password(_accounts, _cache.Cache, _config, _verifier);
        for (var i = 0; i < 3; i++)
        {
            PasswordAttempt attempt = await tcp.CheckAsync("caller", "wrong", LoginSource.FromEndPoint("127.0.0.1:50123"),
                CancellationToken.None);
            Assert.Equal(PasswordCheck.WrongPassword, attempt.Result);
            await tcp.RecordFailureAsync(attempt, CancellationToken.None);
        }

        await Assert.ThrowsAsync<AuthenticationException>(() => LoginAsync("wrong"));
        await Assert.ThrowsAsync<AccountLockedException>(() => LoginAsync("wrong"));

        Account stored = await StoredAsync(account.Id);
        Assert.True(stored.Locked);
        Assert.Equal(5, stored.FailedLogins);
        Assert.Equal(new[] { UsernameKey("caller") }, _cache.UsernameKeys);
        Assert.True(_cache.CountOf(UsernameKey("caller")) > _config.MaxFailedLoginAttempts);
        // The TCP endpoint and the REST address are one source.
        Assert.Equal(5, _cache.CountOf(LoopbackSourceKey));
    }

    /// <summary>
    /// Parallel guesses all read the row before any failure is written. The budget, not the row,
    /// decides, so no more passwords are verified than the limit allows.
    /// </summary>
    [Fact]
    public async Task Verify_no_more_passwords_than_the_limit_for_parallel_guesses()
    {
        Account account = NewAccount();
        account.Id = new AccountId(7);
        IAccountRepository accounts = Substitute.For<IAccountRepository>();
        accounts.FindByUserNameAsync("CALLER", Arg.Any<CancellationToken>()).Returns(account);
        AccountService service = Service(accounts);

        Task[] guesses = Enumerable.Range(0, 25)
            .Select(i => Task.Run(async () =>
            {
                try
                {
                    await service.Authenticate(new AuthenticateRequest { Username = "caller", Password = $"guess-{i}" },
                        IPAddress.Parse($"10.0.{i}.1"), CancellationToken.None);
                }
                catch (AuthenticationException) { }
                catch (AccountLockedException) { }
            }))
            .ToArray();
        await Task.WhenAll(guesses);

        Assert.True(_verifier.Count <= _config.MaxFailedLoginAttempts,
            $"{_verifier.Count} verifies for a limit of {_config.MaxFailedLoginAttempts}");
        // Every lookup of an unlocked account ends in a verify, so the lookups bound the verifies
        // whichever verifier ran them.
        int lookups = accounts.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IAccountRepository.FindByUserNameAsync));
        Assert.True(lookups <= _config.MaxFailedLoginAttempts,
            $"{lookups} lookups (each one verified) for a limit of {_config.MaxFailedLoginAttempts}");
    }

    /// <summary>
    /// An unknown username pays one BCrypt verify and runs the same failure write (against an id no
    /// account has) as a wrong password on a real account, so neither the time taken nor the answer
    /// tells them apart.
    /// </summary>
    [Fact]
    public async Task Take_the_same_path_for_an_unknown_username_as_for_a_known_one()
    {
        Account known = NewAccount();
        known.Id = new AccountId(7);
        IAccountRepository accounts = Substitute.For<IAccountRepository>();
        accounts.FindByUserNameAsync("CALLER", Arg.Any<CancellationToken>()).Returns(known);
        AccountService service = Service(accounts);

        var knownRefusal = await Assert.ThrowsAsync<AuthenticationException>(() => LoginAsync("wrong", "caller", service));
        Assert.Equal(1, _verifier.Count);
        await accounts.Received(1).RecordFailedLoginAsync(new AccountId(7), "127.0.0.1", Arg.Any<DateTime>(), null,
            Arg.Any<CancellationToken>());

        var unknownRefusal = await Assert.ThrowsAsync<AuthenticationException>(() => LoginAsync("wrong", "nobody", service));
        Assert.Equal(2, _verifier.Count);
        await accounts.Received(1).RecordFailedLoginAsync(LoginPolicy.NoAccount, "127.0.0.1", Arg.Any<DateTime>(), null,
            Arg.Any<CancellationToken>());

        Assert.Equal(knownRefusal.Message, unknownRefusal.Message);
    }

    [Fact]
    public async Task Lock_an_unknown_username_as_it_locks_a_known_one()
    {
        for (var i = 1; i < _config.MaxFailedLoginAttempts; i++)
            await Assert.ThrowsAsync<AuthenticationException>(() => LoginAsync("wrong", "nobody"));

        await Assert.ThrowsAsync<AccountLockedException>(() => LoginAsync("wrong", "nobody"));
        await Assert.ThrowsAsync<AccountLockedException>(() => LoginAsync("wrong", "nobody"));
    }

    [Fact]
    public async Task Refuse_a_source_past_its_budget_before_any_lookup()
    {
        await _accounts.CreateAsync(NewAccount());
        for (var i = 0; i < _config.MaxFailedLoginsPerSource; i++)
            await _cache.Cache.IncrementAsync(LoopbackSourceKey, TimeSpan.FromMinutes(15));

        await Assert.ThrowsAsync<AccountLockedException>(() => LoginAsync(Password));

        Assert.Equal(0, _verifier.Count);
        Assert.Empty(_cache.UsernameKeys);
    }

    /// <summary>A completed login clears the username's count and the row's, as the game client's does.</summary>
    [Fact]
    public async Task Clear_the_failure_counts_once_a_login_completes()
    {
        Account account = await _accounts.CreateAsync(NewAccount());
        for (var i = 0; i < 3; i++)
            await Assert.ThrowsAsync<AuthenticationException>(() => LoginAsync("wrong"));

        var (response, accountId) = await LoginAsync(Password);

        Assert.Equal(AuthenticationResponseStatus.Success, response.Status);
        Assert.Equal(account.Id, accountId);
        Assert.False(_cache.Exists(UsernameKey("caller")));
        Assert.Equal(3, _cache.CountOf(LoopbackSourceKey));
        Account stored = await StoredAsync(account.Id);
        Assert.Equal(0, stored.FailedLogins);
        Assert.Equal("127.0.0.1", stored.LastIp);
        // Online is the game client's session flag: a REST login leaves it alone.
        Assert.False(stored.Online);
    }

    /// <summary>
    /// The 403 BANNED/DEACTIVATED answer comes only after the password is proved (#480), and the
    /// attempt keeps its slots, as at the game client's login.
    /// </summary>
    [Fact]
    public async Task Tell_a_banned_account_its_status_only_after_the_password_and_keep_its_slots()
    {
        Account created = NewAccount();
        created.Status = Avalon.Domain.Auth.AccountStatus.Banned;
        await _accounts.CreateAsync(created);

        await Assert.ThrowsAsync<AuthenticationException>(() => LoginAsync("wrong"));
        var refused = await Assert.ThrowsAsync<AccountInactiveException>(() => LoginAsync(Password));

        Assert.Equal("BANNED", refused.Message);
        Assert.Equal(2, _cache.CountOf(UsernameKey("caller")));
    }

    private sealed class CountingVerifier : IPasswordVerifier
    {
        private int _count;

        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _hashes = new();

        public int Count => Volatile.Read(ref _count);

        public IReadOnlyList<string> Hashes => _hashes.ToArray();

        public bool Verify(string password, string hash)
        {
            Interlocked.Increment(ref _count);
            _hashes.Enqueue(hash);
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
    }
}
