using System.Text;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure.Services;
using Avalon.Network.Packets.Auth;
using Avalon.Server.Auth.Configuration;
using Avalon.Server.Auth.Handlers;
using Avalon.Server.Auth.Services;
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
    private const string CorrectPassword = "correct_password";

    private readonly CounterCache _counters = new();
    private readonly IAccountRepository _accounts = Substitute.For<IAccountRepository>();
    private readonly IMfaSetupRepository _mfaSetups = Substitute.For<IMfaSetupRepository>();
    private readonly IMFAHashService _hashes = Substitute.For<IMFAHashService>();
    private readonly IMFAService _mfa = Substitute.For<IMFAService>();
    private readonly CountingVerifier _verifier = new();

    public UsernameBudgetShould()
    {
        _accounts.TryRecordLoginAsync(default!, default!, default, default).ReturnsForAnyArgs(true);
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

        Task[] logins = connections.Select(c => LogInAsync(handler, c, "wrong_password")).ToArray();
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
                await LogInAsync(handler, connection, "wrong_password");
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
            accounts.TryRecordLoginAsync(default!, default!, default, default).ReturnsForAnyArgs(false);
            var handler = new CAuthHandler(NullLoggerFactory.Instance, accounts, counters.Cache, _hashes, _mfaSetups,
                Options(), new CountingVerifier());

            IAuthConnection[] connections = Enumerable.Range(0, 8).Select(ConnectionFrom).ToArray();
            Task[] logins = connections
                .Select((c, i) => LogInAsync(handler, c, i == correctAt ? CorrectPassword : "wrong_password"))
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
            accounts.TryRecordLoginAsync(default!, default!, default, default).ReturnsForAnyArgs(false);
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

    // ── the slot ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Give_back_only_its_own_slot_on_a_correct_password()
    {
        _accounts.FindByUserNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(MakeAccount());
        CAuthHandler handler = PasswordHandler();
        for (var i = 0; i < 3; i++) await LogInAsync(handler, ConnectionFrom(i), "wrong_password");

        IAuthConnection connection = ConnectionFrom(9);
        await LogInAsync(handler, connection, CorrectPassword);

        Assert.Equal(AuthResult.SUCCESS, ResultOf(connection));
        string key = Assert.Single(_counters.UsernameKeys);
        // Three failures, one success that took a slot and gave it back: the failures still count.
        Assert.Equal(3, _counters.CountOf(key));
        await _counters.Cache.Received(1).DecrementFloorAsync(key);
        await _counters.Cache.DidNotReceive().RemoveAsync(key);
    }

    [Fact]
    public async Task Key_the_budget_by_the_normalised_username()
    {
        _accounts.FindByUserNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Account?)null);
        CAuthHandler handler = PasswordHandler();

        await LogInAsync(handler, ConnectionFrom(1), "wrong_password", "  TestUser ");
        await LogInAsync(handler, ConnectionFrom(2), "wrong_password", "testuser");
        await LogInAsync(handler, ConnectionFrom(3), "wrong_password", "TESTUSER");

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

        for (var i = 0; i < Max - 1; i++) await LogInAsync(handler, ConnectionFrom(i), "wrong_password");
        await _counters.Cache.DidNotReceiveWithAnyArgs().KeyExpireAsync(default!, default(TimeSpan));

        await LogInAsync(handler, ConnectionFrom(Max), "wrong_password");

        string key = Assert.Single(_counters.UsernameKeys);
        await _counters.Cache.Received(1).KeyExpireAsync(key, TimeSpan.FromMinutes(15));
    }

    [Fact]
    public async Task Lock_the_account_row_on_the_failure_that_reaches_the_limit_and_not_before()
    {
        Account account = MakeAccount();
        _accounts.FindByUserNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(account);
        CAuthHandler handler = PasswordHandler();

        for (var i = 0; i < Max; i++) await LogInAsync(handler, ConnectionFrom(i), "wrong_password");

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
        for (var i = 0; i < Max; i++) await LogInAsync(handler, ConnectionFrom(i), "wrong_password");

        IAuthConnection connection = ConnectionFrom(20);
        await VerifyCodeAsync(MfaHandler(), connection, "123456");

        Assert.Equal(AuthResult.LOCKED, ResultOf(connection));
        await _mfa.DidNotReceiveWithAnyArgs().VerifyMFAAsync(default!, default!, default);
    }

    [Fact]
    public async Task Give_back_only_its_own_slot_on_a_correct_mfa_code()
    {
        Account account = MakeAccount();
        LiveMfaHashFor(account);
        _mfa.VerifyMFAAsync(Arg.Any<string>(), "123456", Arg.Any<CancellationToken>())
            .Returns(new MFAVerifyResult(true, account.Id));
        _mfa.VerifyMFAAsync(Arg.Any<string>(), "000000", Arg.Any<CancellationToken>())
            .Returns(new MFAVerifyResult(false, null));
        CMFAVerifyHandler handler = MfaHandler();
        for (var i = 0; i < 2; i++) await VerifyCodeAsync(handler, ConnectionFrom(i), "000000");

        IAuthConnection connection = ConnectionFrom(9);
        await VerifyCodeAsync(handler, connection, "123456");

        Assert.Equal(AuthResult.SUCCESS, ResultOf(connection));
        string key = Assert.Single(_counters.UsernameKeys);
        Assert.Equal(2, _counters.CountOf(key));
        await _counters.Cache.Received(1).DecrementFloorAsync(key);
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
