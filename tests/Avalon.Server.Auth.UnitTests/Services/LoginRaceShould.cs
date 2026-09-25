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
using Avalon.Server.Auth.Services;
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
    /// count, so the second reaches the threshold of two and locks the account.
    /// </summary>
    [Fact]
    public async Task Count_both_of_two_failed_logins_that_read_the_same_row()
    {
        Account account = await _accounts.CreateAsync(NewAccount());
        var stale = new StaleAccountRepository(_accounts);
        stale.UsernameReads.Enqueue((await _accounts.FindByUserNameAsync("RACEUSER"))!);
        stale.UsernameReads.Enqueue((await _accounts.FindByUserNameAsync("RACEUSER"))!);

        var handler = new CAuthHandler(NullLoggerFactory.Instance, stale, Substitute.For<IReplicatedCache>(),
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
    /// The same at MFA verify: the account was unlocked when it was read after the code verified,
    /// and locked before the success was written.
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
            Substitute.For<IMFAHashService>(), Options(5));

        await handler.ExecuteAsync(new AuthPacketContext<CMFAVerifyPacket>
        {
            Packet = new CMFAVerifyPacket { MfaHash = "hash", Code = "123456" },
            Connection = connection,
        });

        Account stored = await StoredAsync(account.Id);
        Assert.True(stored.Locked);
        Assert.False(stored.Online);
        Assert.Equal(AuthResult.LOCKED, SentResult(connection));
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

        FailedLoginResult result = await _accounts.RecordFailedLoginAsync(account.Id, "10.0.0.1", DateTime.UtcNow, 5,
            DateTime.UtcNow.AddMinutes(15));

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
        await _accounts.RecordFailedLoginAsync(account.Id, "10.0.0.1", DateTime.UtcNow, 1, firstEnd);

        FailedLoginResult result = await _accounts.RecordFailedLoginAsync(account.Id, "10.0.0.1", DateTime.UtcNow, 1,
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

    /// <summary>
    /// Forwards to the real repository, but can hand out reads taken earlier (a request that read
    /// the row before another wrote it) and run a write of its own right after a read.
    /// </summary>
    private sealed class StaleAccountRepository(IAccountRepository inner) : IAccountRepository
    {
        public Queue<Account> UsernameReads { get; } = new();

        public Func<Task>? AfterRead { get; init; }

        public async Task<Account?> FindByUserNameAsync(string userName, CancellationToken cancellationToken = default)
        {
            Account? account = UsernameReads.Count > 0
                ? UsernameReads.Dequeue()
                : await inner.FindByUserNameAsync(userName, cancellationToken);
            if (AfterRead != null) await AfterRead();
            return account;
        }

        public async Task<Account?> FindByIdAsync(AccountId id, bool track = false, CancellationToken cancellationToken = default)
        {
            Account? account = await inner.FindByIdAsync(id, track, cancellationToken);
            if (AfterRead != null) await AfterRead();
            return account;
        }

        public Task<Account?> FindByEmailAsync(string email, CancellationToken cancellationToken = default) =>
            inner.FindByEmailAsync(email, cancellationToken);

        public Task<FailedLoginResult> RecordFailedLoginAsync(AccountId id, string attemptIp, DateTime now,
            int maxFailedLogins, DateTime lockedUntil, CancellationToken cancellationToken = default) =>
            inner.RecordFailedLoginAsync(id, attemptIp, now, maxFailedLogins, lockedUntil, cancellationToken);

        public Task<bool> TryRecordLoginAsync(AccountId id, string lastIp, DateTime now,
            CancellationToken cancellationToken = default) =>
            inner.TryRecordLoginAsync(id, lastIp, now, cancellationToken);

        public Task<PagedResult<Account>> PaginateAsync(EntityPaginateFilter<Account> filter, bool track = false,
            CancellationToken cancellationToken = default) => inner.PaginateAsync(filter, track, cancellationToken);

        public Task<List<Account>> FindAllAsync(bool track = false, CancellationToken cancellationToken = default) =>
            inner.FindAllAsync(track, cancellationToken);

        public Task<List<Account>> FindByAsync(Expression<Func<Account, bool>> predicate,
            CancellationToken cancellationToken = default) => inner.FindByAsync(predicate, cancellationToken);

        public Task<Account> CreateAsync(Account entity, CancellationToken cancellationToken = default) =>
            inner.CreateAsync(entity, cancellationToken);

        public Task<List<Account>> CreateAsync(List<Account> entities, CancellationToken cancellationToken = default) =>
            inner.CreateAsync(entities, cancellationToken);

        public Task<Account> UpdateAsync(Account entity, CancellationToken cancellationToken = default) =>
            inner.UpdateAsync(entity, cancellationToken);

        public Task DeleteAsync(AccountId id, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(id, cancellationToken);
    }
}
