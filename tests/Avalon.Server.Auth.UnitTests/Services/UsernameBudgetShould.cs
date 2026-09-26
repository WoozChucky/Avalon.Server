using System.Text;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure.Services;
using Avalon.Network.Packets.Auth;
using Avalon.Server.Auth.Configuration;
using Avalon.Server.Auth.Handlers;
using Avalon.Infrastructure.Login;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using ProtoBuf;

namespace Avalon.Server.Auth.UnitTests.Services;

/// <summary>
/// #484: the per-account lock was decided from the row each request read before its BCrypt verify,
/// so every guess that read the row before the lock landed was verified. Spread over many sources,
/// that was about ten guesses per lockout instead of five, and in a parallel batch the right
/// password was the one answered LOCKED while the wrong ones were answered INVALID_CREDENTIALS.
/// A per-username budget, taken before the lookup, now decides.
/// </summary>
public class UsernameBudgetShould
{
    private const int Max = 5;
    private static readonly string CorrectPassword = TestPasswords.Valid;

    private readonly CounterCache _counters = new();
    private readonly IAccountRepository _accounts = Substitute.For<IAccountRepository>();
    private readonly IMfaSetupRepository _mfaSetups = Substitute.For<IMfaSetupRepository>();
    private readonly IMFAHashService _hashes = Substitute.For<IMFAHashService>();
    private readonly IMFAService _mfa = Substitute.For<IMFAService>();
    private readonly CountingVerifier _verifier = new();

    public UsernameBudgetShould()
    {
        _accounts.TryRecordLoginAsync(default!, default!, default, default, default).ReturnsForAnyArgs(true);
    }

    private static IOptions<AuthConfiguration> Options() => Microsoft.Extensions.Options.Options.Create(new AuthConfiguration
    {
        MaxFailedLoginAttempts = Max,
        LockoutDurationMinutes = 15,
        MaxFailedLoginsPerSource = 10,
        FailedLoginSourceWindowMinutes = 15,
        MaxFailedMfaAttempts = 5,
    });

    private CAuthHandler PasswordHandler() => new(NullLoggerFactory.Instance, _accounts, _counters.Cache, _hashes,
        _mfaSetups, Options(), _verifier);

    private CMFAVerifyHandler MfaHandler() => new(NullLoggerFactory.Instance, _mfa, _accounts, _counters.Cache,
        _hashes, Options());

    private static Account MakeAccount() => new()
    {
        Id = new AccountId(1L),
        Username = "TESTUSER",
        Email = "test@test.com",
        Salt = new byte[16],
        Verifier = Encoding.UTF8.GetBytes(CountingVerifier.StoredHash),
        JoinDate = DateTime.UtcNow,
    };

    /// <summary>A connection from its own address, so the per-source budget never stops it.</summary>
    private static IAuthConnection ConnectionFrom(int source)
    {
        IAuthConnection connection = Substitute.For<IAuthConnection>();
        connection.CryptoSession.Returns(new FakeAvalonCryptoSession());
        connection.RemoteEndPoint.Returns($"10.1.{source / 256}.{source % 256}:50000");
        return connection;
    }

    private static AuthResult? ResultOf(IAuthConnection connection)
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

    private static Task LogInAsync(CAuthHandler handler, IAuthConnection connection, string password,
        string username = "testuser") =>
        handler.ExecuteAsync(new AuthPacketContext<CAuthPacket>
        {
            Packet = new CAuthPacket { Username = username, Password = password },
            Connection = connection,
        });

    private static Task VerifyCodeAsync(CMFAVerifyHandler handler, IAuthConnection connection, string code) =>
        handler.ExecuteAsync(new AuthPacketContext<CMFAVerifyPacket>
        {
            Packet = new CMFAVerifyPacket { MfaHash = "hash", Code = code },
            Connection = connection,
        });

    /// <summary>Holds every lookup until released, the shape of connections that all arrive at once.</summary>
    private TaskCompletionSource GateLookups(Account? account)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _accounts.FindByUserNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => gate.Task.ContinueWith(_ => account, TaskScheduler.Default));
        return gate;
    }

    private void LiveMfaHashFor(Account account)
    {
        _hashes.GetAccountIdAsync(Arg.Any<string>()).Returns(account.Id);
        _hashes.RecordAttemptAsync(account.Id).Returns(1L);
        _accounts.FindByIdAsync(account.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(account);
    }

    // ── one username, many sources ────────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Verify_no_more_than_the_account_limit_when_guesses_at_one_username_arrive_in_parallel(bool known)
    {
        TaskCompletionSource gate = GateLookups(known ? MakeAccount() : null);
        CAuthHandler handler = PasswordHandler();
        IAuthConnection[] connections = Enumerable.Range(0, 40).Select(ConnectionFrom).ToArray();

        Task[] logins = connections.Select(c => LogInAsync(handler, c, TestPasswords.Wrong)).ToArray();
        gate.SetResult();
        await Task.WhenAll(logins);

        Assert.InRange(_verifier.Count, 1, Max);
        // The first Max - 1 are wrong guesses; the one that reaches the limit, and every one after
        // it, is told the account is locked.
        Assert.Equal(Max - 1, connections.Count(c => ResultOf(c) == AuthResult.INVALID_CREDENTIALS));
        Assert.Equal(40 - (Max - 1), connections.Count(c => ResultOf(c) == AuthResult.LOCKED));
    }

    /// <summary>
    /// A known and an unknown username take the same steps: one slot, one lookup, one verify, and
    /// the same answer attempt by attempt, including the lock.
    /// </summary>
    [Fact]
    public async Task Answer_a_known_and_an_unknown_username_alike_attempt_by_attempt()
    {
        async Task<(List<AuthResult?> Results, CountingVerifier Verifier, int Lookups)> RunAsync(Account? account)
        {
            var counters = new CounterCache();
            var accounts = Substitute.For<IAccountRepository>();
            accounts.FindByUserNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(account);
            var verifier = new CountingVerifier();
            var handler = new CAuthHandler(NullLoggerFactory.Instance, accounts, counters.Cache, _hashes, _mfaSetups,
                Options(), verifier);
            var results = new List<AuthResult?>();
            for (var i = 0; i < 8; i++)
            {
                IAuthConnection connection = ConnectionFrom(i);
                await LogInAsync(handler, connection, TestPasswords.Wrong);
                results.Add(ResultOf(connection));
            }

            int lookups = accounts.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IAccountRepository.FindByUserNameAsync));
            return (results, verifier, lookups);
        }

        var known = await RunAsync(MakeAccount());
        var unknown = await RunAsync(null);

        AuthResult?[] expected =
        [
            AuthResult.INVALID_CREDENTIALS, AuthResult.INVALID_CREDENTIALS, AuthResult.INVALID_CREDENTIALS,
            AuthResult.INVALID_CREDENTIALS, AuthResult.LOCKED, AuthResult.LOCKED, AuthResult.LOCKED, AuthResult.LOCKED,
        ];
        Assert.Equal(expected, known.Results);
        Assert.Equal(expected, unknown.Results);
        Assert.Equal(Max, known.Verifier.Count);
        Assert.Equal(Max, unknown.Verifier.Count);
        Assert.All(unknown.Verifier.Hashes, h => Assert.Equal(BCryptPasswordVerifier.UnknownAccountHash, h));
        // Refused past the limit before the lookup, known or not.
        Assert.Equal(Max, known.Lookups);
        Assert.Equal(Max, unknown.Lookups);
    }

    /// <summary>
    /// A parallel batch that crosses the lock, with the right password at each position in turn.
    /// The lock lands between its read and its success write, so it is refused; it must be refused
    /// with the answer a wrong password in its place got, never as the one different answer.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    public async Task Answer_the_right_password_in_a_batch_that_crosses_the_lock_as_a_wrong_one_in_its_place(int position)
    {
        async Task<AuthResult?[]> BatchAsync(int? correctAt)
        {
            var counters = new CounterCache();
            var accounts = Substitute.For<IAccountRepository>();
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            accounts.FindByUserNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(_ => gate.Task.ContinueWith(_ => (Account?)MakeAccount(), TaskScheduler.Default));
            // The batch's own failures locked the account after every request had read it.
            accounts.TryRecordLoginAsync(default!, default!, default, default, default).ReturnsForAnyArgs(false);
            var handler = new CAuthHandler(NullLoggerFactory.Instance, accounts, counters.Cache, _hashes, _mfaSetups,
                Options(), new CountingVerifier());

            IAuthConnection[] connections = Enumerable.Range(0, 8).Select(ConnectionFrom).ToArray();
            Task[] logins = connections
                .Select((c, i) => LogInAsync(handler, c, i == correctAt ? CorrectPassword : TestPasswords.Wrong))
                .ToArray();
            gate.SetResult();
            await Task.WhenAll(logins);
            return connections.Select(ResultOf).ToArray();
        }

        AuthResult?[] allWrong = await BatchAsync(null);
        AuthResult?[] withRight = await BatchAsync(position);

        Assert.Equal(allWrong, withRight);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(6)]
    public async Task Answer_the_right_mfa_code_in_a_batch_that_crosses_the_lock_as_a_wrong_one_in_its_place(int position)
    {
        async Task<AuthResult?[]> BatchAsync(int? correctAt)
        {
            var counters = new CounterCache();
            var accounts = Substitute.For<IAccountRepository>();
            Account account = MakeAccount();
            accounts.FindByIdAsync(account.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(account);
            accounts.TryRecordLoginAsync(default!, default!, default, default, default).ReturnsForAnyArgs(false);
            var hashes = Substitute.For<IMFAHashService>();
            hashes.GetAccountIdAsync(Arg.Any<string>()).Returns(account.Id);
            hashes.RecordAttemptAsync(account.Id).Returns(1L);
            // Every code is held at the check until the whole batch has arrived.
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var mfa = Substitute.For<IMFAService>();
            mfa.VerifyMFAAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(ci =>
                {
                    bool right = ci.ArgAt<string>(1) == "123456";
                    return gate.Task.ContinueWith(_ => right
                        ? new MFAVerifyResult(true, account.Id)
                        : new MFAVerifyResult(false, null), TaskScheduler.Default);
                });
            var handler = new CMFAVerifyHandler(NullLoggerFactory.Instance, mfa, accounts, counters.Cache, hashes, Options());

            IAuthConnection[] connections = Enumerable.Range(0, 8).Select(ConnectionFrom).ToArray();
            Task[] verifies = connections
                .Select((c, i) => VerifyCodeAsync(handler, c, i == correctAt ? "123456" : "000000"))
                .ToArray();
            gate.SetResult();
            await Task.WhenAll(verifies);
            return connections.Select(ResultOf).ToArray();
        }

        AuthResult?[] allWrong = await BatchAsync(null);
        AuthResult?[] withRight = await BatchAsync(position);

        Assert.Equal(allWrong, withRight);
    }

    /// <summary>
    /// The same batch on an account with MFA. Inside the budget the right password is answered
    /// MFA_REQUIRED, which singles it out by design: it is a real find within the allowed guesses,
    /// and the code step still has to be passed. Past the budget it is refused like every wrong one.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    public async Task Answer_the_right_password_for_an_mfa_account_in_a_batch_that_crosses_the_lock(int position)
    {
        async Task<AuthResult?[]> BatchAsync(int? correctAt)
        {
            var counters = new CounterCache();
            var accounts = Substitute.For<IAccountRepository>();
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            accounts.FindByUserNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(_ => gate.Task.ContinueWith(_ => (Account?)MakeAccount(), TaskScheduler.Default));
            accounts.TryRecordLoginAsync(default!, default!, default, default, default).ReturnsForAnyArgs(false);
            var setups = Substitute.For<IMfaSetupRepository>();
            setups.FindByAccountIdAsync(Arg.Any<AccountId>(), Arg.Any<CancellationToken>())
                .Returns(new MFASetup { Status = MfaSetupStatus.Confirmed });
            var hashes = Substitute.For<IMFAHashService>();
            hashes.GenerateHashAsync(Arg.Any<Account>()).Returns("hash");
            var handler = new CAuthHandler(NullLoggerFactory.Instance, accounts, counters.Cache, hashes, setups,
                Options(), new CountingVerifier());

            IAuthConnection[] connections = Enumerable.Range(0, 8).Select(ConnectionFrom).ToArray();
            Task[] logins = connections
                .Select((c, i) => LogInAsync(handler, c, i == correctAt ? CorrectPassword : TestPasswords.Wrong))
                .ToArray();
            gate.SetResult();
            await Task.WhenAll(logins);
            return connections.Select(ResultOf).ToArray();
        }

        AuthResult?[] expected = await BatchAsync(null);
        if (position < Max) expected[position] = AuthResult.MFA_REQUIRED;

        Assert.Equal(expected, await BatchAsync(position));
    }

    // ── the slot ──────────────────────────────────────────────────────────

    /// <summary>
    /// Owner decision on #484: a login that completes clears the username's count, so a player's
    /// own typos before it do not carry over and lock them on the next one.
    /// </summary>
    [Fact]
    public async Task Reset_the_username_count_after_a_completed_login()
    {
        _accounts.FindByUserNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(MakeAccount());
        CAuthHandler handler = PasswordHandler();
        for (var i = 0; i < Max - 1; i++) await LogInAsync(handler, ConnectionFrom(i), TestPasswords.Wrong);
        string key = Assert.Single(_counters.UsernameKeys);

        IAuthConnection login = ConnectionFrom(9);
        await LogInAsync(handler, login, CorrectPassword);
        Assert.Equal(AuthResult.SUCCESS, ResultOf(login));
        Assert.False(_counters.Exists(key));

        IAuthConnection typo = ConnectionFrom(10);
        await LogInAsync(handler, typo, TestPasswords.Wrong);
        Assert.Equal(AuthResult.INVALID_CREDENTIALS, ResultOf(typo));
        await _accounts.DidNotReceive().RecordFailedLoginAsync(Arg.Any<AccountId>(), Arg.Any<string>(),
            Arg.Any<DateTime>(), Arg.Is<DateTime?>(d => d != null), Arg.Any<CancellationToken>());
    }

    /// <summary>A completed login clears the username's count, never the source's.</summary>
    [Fact]
    public async Task Never_reset_the_source_count_on_a_completed_login()
    {
        _accounts.FindByUserNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(MakeAccount());
        CAuthHandler handler = PasswordHandler();
        IAuthConnection source = ConnectionFrom(1);
        for (var i = 0; i < 2; i++) await LogInAsync(handler, source, TestPasswords.Wrong);

        await LogInAsync(handler, source, CorrectPassword);

        Assert.Equal(AuthResult.SUCCESS, ResultOf(source));
        string sourceKey = SourceBudget.KeyFor(source.RemoteEndPoint);
        Assert.Equal(2, _counters.CountOf(sourceKey));
        await _counters.Cache.DidNotReceive().RemoveAsync(sourceKey);
    }

    /// <summary>
    /// A correct password on an MFA account is not a completed login: each one makes a fresh MFA
    /// hash, so a reset there would hand out a fresh set of code guesses per password login. It
    /// gives back only its own slot.
    /// </summary>
    [Fact]
    public async Task Give_back_only_its_own_slot_when_a_correct_password_issues_an_mfa_hash()
    {
        _accounts.FindByUserNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(MakeAccount());
        _mfaSetups.FindByAccountIdAsync(Arg.Any<AccountId>(), Arg.Any<CancellationToken>())
            .Returns(new MFASetup { Status = MfaSetupStatus.Confirmed });
        CAuthHandler handler = PasswordHandler();
        for (var i = 0; i < 3; i++) await LogInAsync(handler, ConnectionFrom(i), TestPasswords.Wrong);

        IAuthConnection connection = ConnectionFrom(9);
        await LogInAsync(handler, connection, CorrectPassword);

        Assert.Equal(AuthResult.MFA_REQUIRED, ResultOf(connection));
        string key = Assert.Single(_counters.UsernameKeys);
        Assert.Equal(3, _counters.CountOf(key));
        await _counters.Cache.Received(1).DecrementCounterIfAtMostAsync(key, Max);
        await _counters.Cache.DidNotReceive().DecrementFloorAsync(key);
        await _counters.Cache.DidNotReceive().RemoveAsync(key);
    }

    [Fact]
    public async Task Not_reset_the_count_on_a_correct_password_followed_by_a_wrong_code()
    {
        Account account = MakeAccount();
        _accounts.FindByUserNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(account);
        _mfaSetups.FindByAccountIdAsync(Arg.Any<AccountId>(), Arg.Any<CancellationToken>())
            .Returns(new MFASetup { Status = MfaSetupStatus.Confirmed });
        LiveMfaHashFor(account);
        _mfa.VerifyMFAAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MFAVerifyResult(false, null));
        CAuthHandler handler = PasswordHandler();
        for (var i = 0; i < 3; i++) await LogInAsync(handler, ConnectionFrom(i), TestPasswords.Wrong);

        await LogInAsync(handler, ConnectionFrom(9), CorrectPassword);
        await VerifyCodeAsync(MfaHandler(), ConnectionFrom(10), "000000");

        string key = Assert.Single(_counters.UsernameKeys);
        Assert.Equal(4, _counters.CountOf(key));
        IAuthConnection last = ConnectionFrom(11);
        await LogInAsync(handler, last, TestPasswords.Wrong);
        Assert.Equal(AuthResult.LOCKED, ResultOf(last));
    }

    [Fact]
    public async Task Reset_the_username_count_once_an_mfa_login_completes()
    {
        Account account = MakeAccount();
        LiveMfaHashFor(account);
        _mfa.VerifyMFAAsync(Arg.Any<string>(), "123456", Arg.Any<CancellationToken>())
            .Returns(new MFAVerifyResult(true, account.Id));
        _mfa.VerifyMFAAsync(Arg.Any<string>(), "000000", Arg.Any<CancellationToken>())
            .Returns(new MFAVerifyResult(false, null));
        CMFAVerifyHandler handler = MfaHandler();
        for (var i = 0; i < Max - 1; i++) await VerifyCodeAsync(handler, ConnectionFrom(i), "000000");
        string key = Assert.Single(_counters.UsernameKeys);

        IAuthConnection connection = ConnectionFrom(9);
        await VerifyCodeAsync(handler, connection, "123456");

        Assert.Equal(AuthResult.SUCCESS, ResultOf(connection));
        Assert.False(_counters.Exists(key));
    }

    /// <summary>
    /// #484 review: both slots were given back before the success write, so a right password
    /// refused by a lock that landed mid-login had still returned its source slot, which an attacker
    /// spread across sources can see. The slots stay taken unless the login is recorded.
    /// </summary>
    [Fact]
    public async Task Keep_both_slots_when_a_correct_password_is_refused_by_a_lock_that_landed_mid_login()
    {
        _accounts.FindByUserNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(MakeAccount());
        _accounts.TryRecordLoginAsync(default!, default!, default, default, default).ReturnsForAnyArgs(false);
        IAuthConnection connection = ConnectionFrom(1);

        await LogInAsync(PasswordHandler(), connection, CorrectPassword);

        Assert.Equal(AuthResult.INVALID_CREDENTIALS, ResultOf(connection));
        Assert.Equal(1, _counters.CountOf(SourceBudget.KeyFor(connection.RemoteEndPoint)));
        Assert.Equal(1, _counters.CountOf(Assert.Single(_counters.UsernameKeys)));
        await _counters.Cache.DidNotReceiveWithAnyArgs().DecrementFloorAsync(default!);
        await _counters.Cache.DidNotReceiveWithAnyArgs().DecrementCounterIfAtMostAsync(default!, default);
    }

    [Fact]
    public async Task Keep_both_slots_when_a_correct_code_is_refused_by_a_lock_that_landed_mid_login()
    {
        Account account = MakeAccount();
        LiveMfaHashFor(account);
        _mfa.VerifyMFAAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MFAVerifyResult(true, account.Id));
        _accounts.TryRecordLoginAsync(default!, default!, default, default, default).ReturnsForAnyArgs(false);
        IAuthConnection connection = ConnectionFrom(1);

        await VerifyCodeAsync(MfaHandler(), connection, "123456");

        Assert.Equal(AuthResult.MFA_FAILED, ResultOf(connection));
        Assert.Equal(1, _counters.CountOf(SourceBudget.KeyFor(connection.RemoteEndPoint)));
        Assert.Equal(1, _counters.CountOf(Assert.Single(_counters.UsernameKeys)));
        await _counters.Cache.DidNotReceiveWithAnyArgs().DecrementFloorAsync(default!);
        await _counters.Cache.DidNotReceiveWithAnyArgs().DecrementCounterIfAtMostAsync(default!, default);
    }

    [Fact]
    public async Task Key_the_budget_by_the_normalised_username()
    {
        _accounts.FindByUserNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Account?)null);
        CAuthHandler handler = PasswordHandler();

        await LogInAsync(handler, ConnectionFrom(1), TestPasswords.Wrong, "  TestUser ");
        await LogInAsync(handler, ConnectionFrom(2), TestPasswords.Wrong, "testuser");
        await LogInAsync(handler, ConnectionFrom(3), TestPasswords.Wrong, "TESTUSER");

        string key = Assert.Single(_counters.UsernameKeys);
        Assert.Equal(3, _counters.CountOf(key));
        await _accounts.Received(3).FindByUserNameAsync("TESTUSER", Arg.Any<CancellationToken>());
    }

    /// <summary>The lock is held for the lockout duration from the failure that set it.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Hold_the_budget_for_the_lockout_duration_from_the_failure_that_reaches_the_limit(bool known)
    {
        _accounts.FindByUserNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(known ? MakeAccount() : null);
        CAuthHandler handler = PasswordHandler();

        for (var i = 0; i < Max - 1; i++) await LogInAsync(handler, ConnectionFrom(i), TestPasswords.Wrong);
        string key = Assert.Single(_counters.UsernameKeys);
        _counters.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(TimeSpan.FromMinutes(5), _counters.TimeToLive(key));

        await LogInAsync(handler, ConnectionFrom(Max), TestPasswords.Wrong);

        Assert.Equal(TimeSpan.FromMinutes(15), _counters.TimeToLive(key));
        Assert.Equal(Max + 1, _counters.CountOf(key));
    }

    /// <summary>
    /// #484 review: the hold restarted the expiry of a key that had to still exist. When the key
    /// expired between the take and the hold, the hold did nothing and the budget started again
    /// at one while the row stayed locked, so from then on a known username answered LOCKED and an
    /// unknown one INVALID_CREDENTIALS. The hold now recreates the key.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Keep_refusing_when_the_key_expires_between_the_take_and_the_hold(bool known)
    {
        Account account = MakeAccount();
        _accounts.FindByUserNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(known ? account : null);
        // The row locks as the real repository would.
        _accounts.When(a => a.RecordFailedLoginAsync(Arg.Any<AccountId>(), Arg.Any<string>(), Arg.Any<DateTime>(),
                Arg.Is<DateTime?>(d => d != null), Arg.Any<CancellationToken>()))
            .Do(ci =>
            {
                account.Locked = true;
                account.LockedUntil = ci.ArgAt<DateTime?>(3);
            });
        CAuthHandler handler = PasswordHandler();
        for (var i = 0; i < Max - 1; i++) await LogInAsync(handler, ConnectionFrom(i), TestPasswords.Wrong);
        _counters.BeforeHold = key => _counters.ExpireNow(key);

        await LogInAsync(handler, ConnectionFrom(Max), TestPasswords.Wrong);
        _counters.BeforeHold = null;

        IAuthConnection next = ConnectionFrom(Max + 1);
        await LogInAsync(handler, next, TestPasswords.Wrong);
        Assert.Equal(AuthResult.LOCKED, ResultOf(next));
        Assert.Equal(TimeSpan.FromMinutes(15), _counters.TimeToLive(Assert.Single(_counters.UsernameKeys)));
    }

    /// <summary>
    /// #484 review: a Redis error in the hold skipped the database write, so the row was never
    /// locked. The write runs whatever the hold does, and the error still ends the connection.
    /// </summary>
    [Fact]
    public async Task Lock_the_row_even_when_the_hold_throws()
    {
        Account account = MakeAccount();
        _accounts.FindByUserNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(account);
        CAuthHandler handler = PasswordHandler();
        for (var i = 0; i < Max - 1; i++) await LogInAsync(handler, ConnectionFrom(i), TestPasswords.Wrong);
        _counters.BeforeHold = _ => throw new InvalidOperationException("redis is down");

        IAuthConnection connection = ConnectionFrom(Max);
        await Assert.ThrowsAsync<InvalidOperationException>(() => LogInAsync(handler, connection, TestPasswords.Wrong));

        Assert.Equal(AuthResult.LOCKED, ResultOf(connection));
        await _accounts.Received(1).RecordFailedLoginAsync(account.Id, Arg.Any<string>(), Arg.Any<DateTime>(),
            Arg.Is<DateTime?>(d => d != null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Lock_the_row_even_when_the_hold_throws_at_mfa_verify()
    {
        Account account = MakeAccount();
        LiveMfaHashFor(account);
        _mfa.VerifyMFAAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MFAVerifyResult(false, null));
        CMFAVerifyHandler handler = MfaHandler();
        for (var i = 0; i < Max - 1; i++) await VerifyCodeAsync(handler, ConnectionFrom(i), "000000");
        _counters.BeforeHold = _ => throw new InvalidOperationException("redis is down");

        IAuthConnection connection = ConnectionFrom(Max);
        await Assert.ThrowsAsync<InvalidOperationException>(() => VerifyCodeAsync(handler, connection, "000000"));

        Assert.Equal(AuthResult.LOCKED, ResultOf(connection));
        await _accounts.Received(1).RecordFailedLoginAsync(account.Id, Arg.Any<string>(), Arg.Any<DateTime>(),
            Arg.Is<DateTime?>(d => d != null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Lock_the_account_row_on_the_failure_that_reaches_the_limit_and_not_before()
    {
        Account account = MakeAccount();
        _accounts.FindByUserNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(account);
        CAuthHandler handler = PasswordHandler();

        for (var i = 0; i < Max; i++) await LogInAsync(handler, ConnectionFrom(i), TestPasswords.Wrong);

        await _accounts.Received(Max - 1).RecordFailedLoginAsync(account.Id, Arg.Any<string>(), Arg.Any<DateTime>(),
            (DateTime?)null, Arg.Any<CancellationToken>());
        await _accounts.Received(1).RecordFailedLoginAsync(account.Id, Arg.Any<string>(), Arg.Any<DateTime>(),
            Arg.Is<DateTime?>(d => d != null), Arg.Any<CancellationToken>());
    }

    // ── MFA codes spend the same budget ───────────────────────────────────

    [Fact]
    public async Task Count_wrong_mfa_codes_against_the_username_so_the_password_is_then_refused()
    {
        Account account = MakeAccount();
        LiveMfaHashFor(account);
        _mfa.VerifyMFAAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MFAVerifyResult(false, null));
        _accounts.FindByUserNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(account);
        CMFAVerifyHandler mfaHandler = MfaHandler();

        for (var i = 0; i < Max; i++) await VerifyCodeAsync(mfaHandler, ConnectionFrom(i), "000000");

        IAuthConnection connection = ConnectionFrom(20);
        await LogInAsync(PasswordHandler(), connection, CorrectPassword);

        Assert.Equal(AuthResult.LOCKED, ResultOf(connection));
        Assert.Equal(0, _verifier.Count);
    }

    [Fact]
    public async Task Refuse_an_mfa_code_past_the_username_limit_without_checking_it()
    {
        Account account = MakeAccount();
        LiveMfaHashFor(account);
        _mfa.VerifyMFAAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MFAVerifyResult(true, account.Id));
        _accounts.FindByUserNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(account);
        CAuthHandler handler = PasswordHandler();
        for (var i = 0; i < Max; i++) await LogInAsync(handler, ConnectionFrom(i), TestPasswords.Wrong);

        IAuthConnection connection = ConnectionFrom(20);
        await VerifyCodeAsync(MfaHandler(), connection, "123456");

        Assert.Equal(AuthResult.LOCKED, ResultOf(connection));
        await _mfa.DidNotReceiveWithAnyArgs().VerifyMFAAsync(default!, default!, default);
    }

    /// <summary>
    /// #484 re-review: the completed-login reset deleted the key whatever it held. A login recorded
    /// just before a concurrent failure held the budget at the limit (and locked the row) would
    /// delete that hold, and the row would stay locked with nothing refusing an unknown username the
    /// same way. The reset deletes the key only while it is below the limit.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Keep_a_held_budget_when_a_login_completes_as_the_lock_lands(bool mfa)
    {
        Account account = MakeAccount();
        _accounts.FindByUserNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(account);
        LiveMfaHashFor(account);
        _mfa.VerifyMFAAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MFAVerifyResult(true, account.Id));
        string key = UsernameBudget.KeyFor("testuser");
        // A concurrent last-slot failure holds the budget right after this login was recorded.
        _accounts.TryRecordLoginAsync(default!, default!, default, default, default).ReturnsForAnyArgs(_ =>
        {
            UsernameBudget.HoldLockAsync(_counters.Cache, Options().Value, key);
            return true;
        });
        IAuthConnection connection = ConnectionFrom(1);

        if (mfa) await VerifyCodeAsync(MfaHandler(), connection, "123456");
        else await LogInAsync(PasswordHandler(), connection, CorrectPassword);

        Assert.Equal(AuthResult.SUCCESS, ResultOf(connection));
        Assert.True(_counters.Exists(key));
        Assert.Equal(Max + 1, _counters.CountOf(key));
    }

    /// <summary>
    /// Starts logins that each take their slot at once and then wait at the lookup until released,
    /// one at a time, so a test can fix the order the steps after the take run in.
    /// </summary>
    private sealed class OrderedLogins
    {
        private readonly List<TaskCompletionSource> _gates = new();
        private readonly List<Task> _logins = new();
        private int _lookups = -1;

        public OrderedLogins(IAccountRepository accounts, Account account)
        {
            accounts.FindByUserNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ =>
            {
                TaskCompletionSource gate = _gates[Interlocked.Increment(ref _lookups)];
                return gate.Task.ContinueWith(_ => (Account?)account, TaskScheduler.Default);
            });
        }

        /// <summary>Starts a login; it takes its slot before this returns. Returns its index.</summary>
        public int Start(Func<Task> login)
        {
            _gates.Add(new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            _logins.Add(login());
            return _logins.Count - 1;
        }

        /// <summary>Lets one login past its lookup and waits for it to finish.</summary>
        public async Task RunAsync(int index)
        {
            _gates[index].SetResult();
            await _logins[index];
        }
    }

    /// <summary>
    /// #484 re-review of the reset: the give-back at MFA_REQUIRED was a plain floored DECR, so it
    /// lowered a hold. B (right password, MFA account) takes 4, A (wrong) takes 5 and holds the
    /// count at 6, B gives back to 5, and C (right password, no MFA) that had taken 3 completes its
    /// login and its reset deleted the key. The username give-back leaves a held count alone.
    /// </summary>
    [Fact]
    public async Task Keep_a_hold_when_an_mfa_give_back_and_a_reset_follow_it()
    {
        Account account = MakeAccount();
        string key = UsernameBudget.KeyFor("testuser");
        for (var i = 0; i < 2; i++) await _counters.Cache.IncrementAsync(key, TimeSpan.FromMinutes(15));
        _mfaSetups.FindByAccountIdAsync(Arg.Any<AccountId>(), Arg.Any<CancellationToken>())
            .Returns(new MFASetup { Status = MfaSetupStatus.Confirmed }, (MFASetup?)null);
        _hashes.GenerateHashAsync(Arg.Any<Account>()).Returns("hash");
        var logins = new OrderedLogins(_accounts, account);
        CAuthHandler handler = PasswordHandler();
        IAuthConnection c = ConnectionFrom(1), b = ConnectionFrom(2), a = ConnectionFrom(3);

        int cIndex = logins.Start(() => LogInAsync(handler, c, CorrectPassword)); // slot 3
        int bIndex = logins.Start(() => LogInAsync(handler, b, CorrectPassword)); // slot 4
        int aIndex = logins.Start(() => LogInAsync(handler, a, TestPasswords.Wrong)); // slot 5
        await logins.RunAsync(aIndex);
        Assert.Equal(Max + 1, _counters.CountOf(key));
        await logins.RunAsync(bIndex);
        await logins.RunAsync(cIndex);

        Assert.Equal(AuthResult.LOCKED, ResultOf(a));
        Assert.Equal(AuthResult.MFA_REQUIRED, ResultOf(b));
        Assert.Equal(AuthResult.SUCCESS, ResultOf(c));
        Assert.Equal(Max + 1, _counters.CountOf(key));
    }

    /// <summary>
    /// The same give-back, twice: two MFA password steps that took slots before the hold would bring
    /// it from 6 to 4, and the next guess would be verified again.
    /// </summary>
    [Fact]
    public async Task Keep_a_hold_when_two_mfa_give_backs_follow_it()
    {
        Account account = MakeAccount();
        string key = UsernameBudget.KeyFor("testuser");
        for (var i = 0; i < 2; i++) await _counters.Cache.IncrementAsync(key, TimeSpan.FromMinutes(15));
        _mfaSetups.FindByAccountIdAsync(Arg.Any<AccountId>(), Arg.Any<CancellationToken>())
            .Returns(new MFASetup { Status = MfaSetupStatus.Confirmed });
        _hashes.GenerateHashAsync(Arg.Any<Account>()).Returns("hash");
        var logins = new OrderedLogins(_accounts, account);
        CAuthHandler handler = PasswordHandler();

        int b1 = logins.Start(() => LogInAsync(handler, ConnectionFrom(1), CorrectPassword)); // slot 3
        int b2 = logins.Start(() => LogInAsync(handler, ConnectionFrom(2), CorrectPassword)); // slot 4
        int a = logins.Start(() => LogInAsync(handler, ConnectionFrom(3), TestPasswords.Wrong)); // slot 5
        await logins.RunAsync(a);
        await logins.RunAsync(b1);
        await logins.RunAsync(b2);

        Assert.Equal(Max + 1, _counters.CountOf(key));
        int verifies = _verifier.Count;
        IAuthConnection next = ConnectionFrom(4);
        logins.Start(() => LogInAsync(handler, next, TestPasswords.Wrong));
        Assert.Equal(AuthResult.LOCKED, ResultOf(next));
        Assert.Equal(verifies, _verifier.Count);
    }

    /// <summary>
    /// #484 re-review: the row lock was written with the request's token, so a connection closing
    /// at that moment could skip the lock. A lock write never takes it.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Write_the_row_lock_without_the_request_token(bool mfa)
    {
        Account account = MakeAccount();
        _accounts.FindByUserNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(account);
        LiveMfaHashFor(account);
        _mfa.VerifyMFAAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MFAVerifyResult(false, null));
        using var closing = new CancellationTokenSource();

        for (var i = 0; i < Max; i++)
        {
            IAuthConnection connection = ConnectionFrom(i);
            if (mfa)
            {
                await MfaHandler().ExecuteAsync(new AuthPacketContext<CMFAVerifyPacket>
                {
                    Packet = new CMFAVerifyPacket { MfaHash = "hash", Code = "000000" },
                    Connection = connection,
                }, closing.Token);
            }
            else
            {
                await PasswordHandler().ExecuteAsync(new AuthPacketContext<CAuthPacket>
                {
                    Packet = new CAuthPacket { Username = "testuser", Password = TestPasswords.Wrong },
                    Connection = connection,
                }, closing.Token);
            }
        }

        await _accounts.Received(1).RecordFailedLoginAsync(account.Id, Arg.Any<string>(), Arg.Any<DateTime>(),
            Arg.Is<DateTime?>(d => d != null), CancellationToken.None);
        await _accounts.DidNotReceive().RecordFailedLoginAsync(Arg.Any<AccountId>(), Arg.Any<string>(),
            Arg.Any<DateTime>(), Arg.Is<DateTime?>(d => d != null), closing.Token);
    }

    /// <summary>
    /// #484 re-review: with the hold's error left to propagate through the finally, a failing row
    /// write replaced it and the Redis error was never seen. It is logged before the row is written.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Log_the_hold_error_even_when_the_row_write_fails_too(bool mfa)
    {
        Account account = MakeAccount();
        _accounts.FindByUserNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(account);
        LiveMfaHashFor(account);
        _mfa.VerifyMFAAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MFAVerifyResult(false, null));
        _accounts.RecordFailedLoginAsync(Arg.Any<AccountId>(), Arg.Any<string>(), Arg.Any<DateTime>(),
                Arg.Is<DateTime?>(d => d != null), Arg.Any<CancellationToken>())
            .Returns<Task<FailedLoginResult>>(_ => throw new TimeoutException("database is down"));
        var redisDown = new InvalidOperationException("redis is down");
        ILogger logger = Substitute.For<ILogger>();
        ILoggerFactory loggers = Substitute.For<ILoggerFactory>();
        loggers.CreateLogger(Arg.Any<string>()).Returns(logger);
        var password = new CAuthHandler(loggers, _accounts, _counters.Cache, _hashes, _mfaSetups, Options(), _verifier);
        var code = new CMFAVerifyHandler(loggers, _mfa, _accounts, _counters.Cache, _hashes, Options());
        for (var i = 0; i < Max - 1; i++)
        {
            if (mfa) await VerifyCodeAsync(code, ConnectionFrom(i), "000000");
            else await LogInAsync(password, ConnectionFrom(i), TestPasswords.Wrong);
        }
        _counters.BeforeHold = _ => throw redisDown;

        IAuthConnection connection = ConnectionFrom(Max);
        await Assert.ThrowsAnyAsync<Exception>(() => mfa
            ? VerifyCodeAsync(code, connection, "000000")
            : LogInAsync(password, connection, TestPasswords.Wrong));

        Assert.Contains(logger.ReceivedCalls(), c => c.GetMethodInfo().Name == nameof(ILogger.Log)
            && (LogLevel)c.GetArguments()[0]! == LogLevel.Error
            && ReferenceEquals(c.GetArguments()[3], redisDown));
        await _accounts.Received(1).RecordFailedLoginAsync(account.Id, Arg.Any<string>(), Arg.Any<DateTime>(),
            Arg.Is<DateTime?>(d => d != null), Arg.Any<CancellationToken>());
    }

    // ── Redis failing ─────────────────────────────────────────────────────

    /// <summary>
    /// A budget that cannot be taken fails closed: the handler throws before any lookup or verify,
    /// and the server closes the connection, so nothing is ever answered SUCCESS.
    /// </summary>
    [Fact]
    public async Task Close_the_connection_without_verifying_when_the_username_budget_cannot_be_taken()
    {
        IReplicatedCache cache = Substitute.For<IReplicatedCache>();
        cache.IncrementAsync(Arg.Is<string>(k => k.StartsWith("auth:username:", StringComparison.Ordinal)), Arg.Any<TimeSpan>())
            .Returns<Task<long>>(_ => throw new InvalidOperationException("redis is down"));
        _accounts.FindByUserNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(MakeAccount());
        ServiceProvider services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(_accounts)
            .AddSingleton(cache)
            .AddSingleton(_hashes)
            .AddSingleton(_mfaSetups)
            .AddSingleton(Options())
            .AddSingleton<IPasswordVerifier>(_verifier)
            .BuildServiceProvider();
        IPacketManager packets = Substitute.For<IPacketManager>();
        packets.TryGetPacketInfo(NetworkPacketType.CMSG_AUTH, out Arg.Any<PacketInfo>())
            .Returns(ci =>
            {
                ci[1] = new PacketInfo(typeof(CAuthPacket), typeof(CAuthHandler));
                return true;
            });
        var hosting = Substitute.For<IOptions<HostingConfiguration>>();
        hosting.Value.Returns(new HostingConfiguration { Port = 0, Host = "127.0.0.1" });
        var security = Substitute.For<IOptions<HostingSecurity>>();
        security.Value.Returns(new HostingSecurity());
        var server = new AuthServer(services, packets, NullLoggerFactory.Instance, _accounts,
            Substitute.For<IReplicatedCache>(), hosting, security);
        IAuthConnection connection = ConnectionFrom(1);

        await server.CallListener(connection, new NetworkPacketHeader { Type = NetworkPacketType.CMSG_AUTH },
            new CAuthPacket { Username = "testuser", Password = CorrectPassword });

        connection.Received(1).Close();
        Assert.Equal(0, _verifier.Count);
        Assert.Null(ResultOf(connection));
        await _accounts.DidNotReceiveWithAnyArgs().FindByUserNameAsync(default!, default);
        await _accounts.DidNotReceiveWithAnyArgs().TryRecordLoginAsync(default!, default!, default, default, default);
    }

    /// <summary>
    /// Returns true only for the correct password against the stored hash, and counts every call.
    /// A real BCrypt verify is what the handler pays for; its cost is not what these tests measure.
    /// </summary>
    private sealed class CountingVerifier : IPasswordVerifier
    {
        public const string StoredHash = "stored-hash";
        private int _count;
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _hashes = new();

        public int Count => Volatile.Read(ref _count);

        public IEnumerable<string> Hashes => _hashes;

        public bool Verify(string password, string hash)
        {
            Interlocked.Increment(ref _count);
            _hashes.Enqueue(hash);
            return hash == StoredHash && password == CorrectPassword;
        }
    }
}
