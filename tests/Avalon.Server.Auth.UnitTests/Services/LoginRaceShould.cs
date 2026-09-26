using System.Linq.Expressions;
using System.Text;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Services;
using Avalon.Network.Packets.Auth;
using Avalon.Server.Auth.Configuration;
using Avalon.Server.Auth.Handlers;
using Avalon.Infrastructure.Login;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using ProtoBuf;

namespace Avalon.Server.Auth.UnitTests.Services;

/// <summary>
/// #471 review: the login wrote the whole account row from the copy it had read, so two requests
/// that read the same row overwrote one another. Failed logins were lost, and a success or a
/// verified MFA code could erase a lock set in between. These run the real repository over a real
/// relational schema, with the read made stale on purpose, because the fix is in the SQL.
/// </summary>
public sealed class LoginRaceShould : IDisposable
{
    private readonly AuthSqlite _database = new();
    private readonly AccountRepository _accounts;

    public LoginRaceShould()
    {
        _accounts = new AccountRepository(_database);
    }

    public void Dispose() => _database.Dispose();

    private static Account NewAccount() => new()
    {
        Username = "RACEUSER",
        Email = "race@example.com",
        Salt = new byte[16],
        Verifier = Encoding.UTF8.GetBytes(BCrypt.Net.BCrypt.HashPassword("correct_password")),
        JoinDate = DateTime.UtcNow,
        LastLogin = DateTime.UtcNow,
    };

    private async Task<Account> StoredAsync(AccountId id)
    {
        await using AuthDbContext context = _database.CreateDbContext();
        return await context.Accounts.AsNoTracking().SingleAsync(a => a.Id == id);
    }

    private static IAuthConnection Connection()
    {
        IAuthConnection connection = Substitute.For<IAuthConnection>();
        connection.CryptoSession.Returns(new FakeAvalonCryptoSession());
        connection.RemoteEndPoint.Returns("127.0.0.1:12345");
        return connection;
    }

    private static AuthResult? SentResult(IAuthConnection connection)
    {
        NetworkPacket? sent = connection.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IAuthConnection.Send))
            .Select(c => c.GetArguments()[0])
            .OfType<NetworkPacket>()
            .LastOrDefault();
        if (sent == null) return null;
        using var stream = new MemoryStream(sent.Payload);
        return Serializer.Deserialize<SAuthResultPacket>(stream).Result;
    }

    private static IOptions<AuthConfiguration> Options(int maxFailedLogins) =>
        Microsoft.Extensions.Options.Options.Create(new AuthConfiguration { MaxFailedLoginAttempts = maxFailedLogins });

    /// <summary>
    /// Both wrong passwords read the row at FailedLogins = 0 before either wrote. Each one must still
    /// count, so the second reaches the threshold of two (its slot in the username budget) and locks
    /// the account.
    /// </summary>
    [Fact]
    public async Task Count_both_of_two_failed_logins_that_read_the_same_row()
    {
        Account account = await _accounts.CreateAsync(NewAccount());
        var stale = new StaleAccountRepository(_accounts);
        stale.UsernameReads.Enqueue((await _accounts.FindByUserNameAsync("RACEUSER"))!);
        stale.UsernameReads.Enqueue((await _accounts.FindByUserNameAsync("RACEUSER"))!);

        var handler = new CAuthHandler(NullLoggerFactory.Instance, stale, new CounterCache().Cache,
            Substitute.For<IMFAHashService>(), Substitute.For<IMfaSetupRepository>(), Options(2),
            new BCryptPasswordVerifier());

        foreach (var _ in new[] { 1, 2 })
        {
            await handler.ExecuteAsync(new AuthPacketContext<CAuthPacket>
            {
                Packet = new CAuthPacket { Username = "raceuser", Password = "wrong_password" },
                Connection = Connection(),
            });
        }

        Account stored = await StoredAsync(account.Id);
        Assert.Equal(2, stored.FailedLogins);
        Assert.True(stored.Locked);
        Assert.NotNull(stored.LockedUntil);
    }

    /// <summary>
    /// The password was right when the row was read, but failed logins locked the account before the
    /// success was written. The success must not write that lock away.
    /// </summary>
    [Fact]
    public async Task Not_erase_a_lock_set_after_a_correct_password_was_read()
    {
        Account account = await _accounts.CreateAsync(NewAccount());
        var stale = new StaleAccountRepository(_accounts)
        {
            AfterRead = () => LockAsync(account.Id),
        };

        var connection = Connection();
        var handler = new CAuthHandler(NullLoggerFactory.Instance, stale, Substitute.For<IReplicatedCache>(),
            Substitute.For<IMFAHashService>(), Substitute.For<IMfaSetupRepository>(), Options(5),
            new BCryptPasswordVerifier());

        await handler.ExecuteAsync(new AuthPacketContext<CAuthPacket>
        {
            Packet = new CAuthPacket { Username = "raceuser", Password = "correct_password" },
            Connection = connection,
        });

        Account stored = await StoredAsync(account.Id);
        Assert.True(stored.Locked);
        Assert.False(stored.Online);
        Assert.NotEqual(AuthResult.SUCCESS, SentResult(connection));
    }

    /// <summary>
    /// The same at MFA verify: the account was unlocked when it was read, and locked before the
    /// success was written. The refusal is the answer a wrong code in this attempt's budget slot
    /// gets (#484), here the first slot's MFA_FAILED, so a parallel batch that crosses the lock does
    /// not single out the right code.
    /// </summary>
    [Fact]
    public async Task Not_erase_a_lock_set_while_an_mfa_code_was_being_verified()
    {
        Account account = await _accounts.CreateAsync(NewAccount());
        var stale = new StaleAccountRepository(_accounts)
        {
            AfterRead = () => LockAsync(account.Id),
        };
        IMFAService mfa = Substitute.For<IMFAService>();
        mfa.VerifyMFAAsync("hash", "123456", Arg.Any<CancellationToken>())
            .Returns(new MFAVerifyResult(true, account.Id));

        var connection = Connection();
        var handler = new CMFAVerifyHandler(NullLoggerFactory.Instance, mfa, stale, Substitute.For<IReplicatedCache>(),
            LiveHash(account.Id), Options(5));

        await handler.ExecuteAsync(new AuthPacketContext<CMFAVerifyPacket>
        {
            Packet = new CMFAVerifyPacket { MfaHash = "hash", Code = "123456" },
            Connection = connection,
        });

        Account stored = await StoredAsync(account.Id);
        Assert.True(stored.Locked);
        Assert.False(stored.Online);
        Assert.Equal(AuthResult.MFA_FAILED, SentResult(connection));
    }

    /// <summary>A live MFA hash for the account, each code the first attempt on a fresh hash.</summary>
    private static IMFAHashService LiveHash(AccountId id)
    {
        IMFAHashService hashes = Substitute.For<IMFAHashService>();
        hashes.GetAccountIdAsync(Arg.Any<string>()).Returns(id);
        hashes.RecordAttemptAsync(id).Returns(1L);
        return hashes;
    }

    /// <summary>
    /// Re-review: logging in again with the right password makes a fresh hash with no attempts, so
    /// the per-hash cap alone never ends a guessing run. Wrong codes, one per fresh hash, must still
    /// lock the account at the per-account threshold.
    /// </summary>
    [Fact]
    public async Task Lock_the_account_after_enough_wrong_codes_across_fresh_mfa_hashes()
    {
        Account account = await _accounts.CreateAsync(NewAccount());
        IMFAService mfa = Substitute.For<IMFAService>();
        mfa.VerifyMFAAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MFAVerifyResult(false, null));
        var handler = new CMFAVerifyHandler(NullLoggerFactory.Instance, mfa, _accounts, new CounterCache().Cache,
            LiveHash(account.Id), Options(5));

        for (var i = 0; i < 5; i++)
        {
            await handler.ExecuteAsync(new AuthPacketContext<CMFAVerifyPacket>
            {
                Packet = new CMFAVerifyPacket { MfaHash = $"hash-{i}", Code = "000000" },
                Connection = Connection(),
            });
        }

        Account stored = await StoredAsync(account.Id);
        Assert.Equal(5, stored.FailedLogins);
        Assert.True(stored.Locked);
        Assert.NotNull(stored.LockedUntil);
    }

    [Fact]
    public async Task Lift_an_expired_lock_and_count_the_failure_as_the_first()
    {
        Account account = await _accounts.CreateAsync(NewAccount());
        await using (AuthDbContext context = _database.CreateDbContext())
        {
            await context.Accounts.Where(a => a.Id == account.Id).ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Locked, true)
                .SetProperty(a => a.FailedLogins, 5)
                .SetProperty(a => a.LockedUntil, DateTime.UtcNow.AddMinutes(-1)));
        }

        FailedLoginResult result = await _accounts.RecordFailedLoginAsync(account.Id, "10.0.0.1", DateTime.UtcNow,
            null);

        Assert.Equal(new FailedLoginResult(1, false), result);
        Account stored = await StoredAsync(account.Id);
        Assert.Null(stored.LockedUntil);
        Assert.Equal("10.0.0.1", stored.LastAttemptIp);
    }

    [Fact]
    public async Task Keep_an_existing_lock_and_its_end_when_another_failure_arrives()
    {
        Account account = await _accounts.CreateAsync(NewAccount());
        DateTime firstEnd = DateTime.UtcNow.AddMinutes(10);
        await _accounts.RecordFailedLoginAsync(account.Id, "10.0.0.1", DateTime.UtcNow, firstEnd);

        FailedLoginResult result = await _accounts.RecordFailedLoginAsync(account.Id, "10.0.0.1", DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(30));

        Assert.True(result.Locked);
        Assert.Equal(2, result.FailedLogins);
        Account stored = await StoredAsync(account.Id);
        Assert.Equal(firstEnd, stored.LockedUntil!.Value, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Record_a_login_only_on_an_account_that_is_not_locked_now()
    {
        Account account = await _accounts.CreateAsync(NewAccount());
        await LockAsync(account.Id);

        Assert.False(await _accounts.TryRecordLoginAsync(account.Id, "10.0.0.2", DateTime.UtcNow));
        Assert.True(await _accounts.TryRecordLoginAsync(account.Id, "10.0.0.2", DateTime.UtcNow.AddMinutes(20)));

        Account stored = await StoredAsync(account.Id);
        Assert.True(stored.Online);
        Assert.False(stored.Locked);
        Assert.Null(stored.LockedUntil);
        Assert.Equal(0, stored.FailedLogins);
        Assert.Equal("10.0.0.2", stored.LastIp);
    }

    private async Task LockAsync(AccountId id)
    {
        await using AuthDbContext context = _database.CreateDbContext();
        await context.Accounts.Where(a => a.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(a => a.Locked, true)
            .SetProperty(a => a.FailedLogins, 5)
            .SetProperty(a => a.LockedUntil, DateTime.UtcNow.AddMinutes(15)));
    }
}
