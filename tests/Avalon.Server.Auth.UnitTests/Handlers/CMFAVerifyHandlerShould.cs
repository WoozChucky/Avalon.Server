using Avalon.Common.Cryptography;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Services;
using Avalon.Network.Packets.Auth;
using Avalon.Server.Auth;
using Avalon.Server.Auth.Configuration;
using Avalon.Server.Auth.Handlers;
using Avalon.Server.Auth.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using ProtoBuf;

namespace Avalon.Server.Auth.UnitTests.Handlers;

public class CMFAVerifyHandlerShould
{
    private readonly IMFAService _mfaService = Substitute.For<IMFAService>();
    private readonly IAccountRepository _accountRepository = Substitute.For<IAccountRepository>();
    private readonly IReplicatedCache _cache = Substitute.For<IReplicatedCache>();
    private readonly IAuthConnection _connection = Substitute.For<IAuthConnection>();
    private readonly IAvalonCryptoSession _cryptoSession = new FakeAvalonCryptoSession();

    private readonly IMFAHashService _mfaHashService = Substitute.For<IMFAHashService>();

    private CMFAVerifyHandler CreateHandler(int maxPerSource = 10, int maxMfaAttempts = 5) =>
        new(NullLoggerFactory.Instance, _mfaService, _accountRepository, _cache, _mfaHashService,
            Options.Create(new AuthConfiguration
            {
                MaxFailedLoginsPerSource = maxPerSource,
                MaxFailedMfaAttempts = maxMfaAttempts,
            }));

    private static Account MakeAccount(long id = 1) => new()
    {
        Id = new AccountId(id),
        Email = "test@test.com",
        Username = "TESTUSER",
        Salt = new byte[16],
        Verifier = new byte[16],
        JoinDate = DateTime.UtcNow
    };

    public CMFAVerifyHandlerShould()
    {
        _connection.CryptoSession.Returns(_cryptoSession);
        _connection.RemoteEndPoint.Returns("127.0.0.1:12345");
        _accountRepository.TryRecordLoginAsync(default!, default!, default, default).ReturnsForAnyArgs(true);
        // A live MFA hash for account 1 with its first attempt, unless a test says otherwise.
        _mfaHashService.GetAccountIdAsync(Arg.Any<string>()).Returns(new AccountId(1L));
        _mfaHashService.RecordAttemptAsync(Arg.Any<AccountId>()).Returns(1L);
        // The hash's account exists, unless a test says otherwise.
        _accountRepository.FindByIdAsync(Arg.Any<AccountId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(ci => MakeAccount(ci.ArgAt<AccountId>(0).Value));
    }

    [Fact]
    public async Task SendSuccess_AndSetAccountOnline_WhenCodeIsValid()
    {
        var account = MakeAccount();
        var accountId = new AccountId(1L);
        _mfaService.VerifyMFAAsync("valid-hash", "123456").Returns(new MFAVerifyResult(true, accountId));
        _accountRepository.FindByIdAsync(accountId).Returns(account);

        var ctx = new AuthPacketContext<CMFAVerifyPacket>
        {
            Packet = new CMFAVerifyPacket { MfaHash = "valid-hash", Code = "123456" },
            Connection = _connection
        };

        await CreateHandler().ExecuteAsync(ctx);

        Assert.True(account.Online);
        _connection.Received(1).Send(Arg.Any<NetworkPacket>());
        _connection.Received().AccountId = accountId;
        await _cache.Received(1).PublishAsync(CacheKeys.AuthAccountsOnlineChannel, Arg.Any<string>());
        await _accountRepository.Received(1).TryRecordLoginAsync(accountId, "127.0.0.1", Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
        await _accountRepository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task SendMfaFailed_WhenHashNotFoundOrExpired()
    {
        _mfaService.VerifyMFAAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(new MFAVerifyResult(false, null));

        var ctx = new AuthPacketContext<CMFAVerifyPacket>
        {
            Packet = new CMFAVerifyPacket { MfaHash = "bad-hash", Code = "000000" },
            Connection = _connection
        };

        await CreateHandler().ExecuteAsync(ctx);

        _connection.Received(1).Send(Arg.Any<NetworkPacket>());
        await _accountRepository.DidNotReceive().UpdateAsync(Arg.Any<Account>());
    }

    [Fact]
    public async Task SendMfaFailed_WhenCodeIsInvalid()
    {
        _mfaService.VerifyMFAAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(new MFAVerifyResult(false, null));

        var ctx = new AuthPacketContext<CMFAVerifyPacket>
        {
            Packet = new CMFAVerifyPacket { MfaHash = "valid-hash", Code = "wrong" },
            Connection = _connection
        };

        await CreateHandler().ExecuteAsync(ctx);

        _connection.Received(1).Send(Arg.Any<NetworkPacket>());
        await _mfaService.Received(1).VerifyMFAAsync("valid-hash", "wrong");
    }

    [Fact]
    public async Task SendMfaFailed_WhenAccountNotFoundAfterVerify()
    {
        var accountId = new AccountId(1L);
        _mfaService.VerifyMFAAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(new MFAVerifyResult(true, accountId));
        _accountRepository.FindByIdAsync(accountId).Returns((Account?)null);

        var ctx = new AuthPacketContext<CMFAVerifyPacket>
        {
            Packet = new CMFAVerifyPacket { MfaHash = "valid-hash", Code = "123456" },
            Connection = _connection
        };

        await CreateHandler().ExecuteAsync(ctx);

        _connection.Received(1).Send(Arg.Any<NetworkPacket>());
        await _accountRepository.DidNotReceive().UpdateAsync(Arg.Any<Account>());
        await _cache.DidNotReceive().PublishAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task SendAlreadyConnected_WhenAccountIsOnlineAfterVerify()
    {
        var accountId = new AccountId(42L);
        var account = MakeAccount(id: 42L);
        account.Online = true;

        _mfaHashService.GetAccountIdAsync(Arg.Any<string>()).Returns(accountId);
        _mfaService.VerifyMFAAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(new MFAVerifyResult(true, accountId));
        _accountRepository.FindByIdAsync(accountId).Returns(account);

        // Server with empty connections list — no connected session found
        var hostingOptions = Substitute.For<IOptions<HostingConfiguration>>();
        hostingOptions.Value.Returns(new HostingConfiguration { Port = 0, Host = "127.0.0.1" });
        var securityOptions = Substitute.For<IOptions<HostingSecurity>>();
        securityOptions.Value.Returns(new HostingSecurity());
        var server = new AuthServer(
            Substitute.For<IServiceProvider>(),
            Substitute.For<IPacketManager>(),
            NullLoggerFactory.Instance,
            Substitute.For<IAccountRepository>(),
            hostingOptions,
            securityOptions);
        _connection.Server.Returns(server);

        var ctx = new AuthPacketContext<CMFAVerifyPacket>
        {
            Packet = new CMFAVerifyPacket { MfaHash = "valid-hash", Code = "123456" },
            Connection = _connection
        };

        await CreateHandler().ExecuteAsync(ctx);

        _connection.Received(1).Send(Arg.Any<NetworkPacket>());
        await _cache.Received(1).PublishAsync("world:accounts:disconnect", Arg.Any<string>());
        Assert.False(account.Online);
        // Only the Online flag, never the whole row (#484).
        await _accountRepository.Received(1).MarkOfflineAsync(accountId, 0, Arg.Any<CancellationToken>());
        await _accountRepository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    /// <summary>
    /// #462: the password step refuses a non-Active account, but the MFA hash it hands out lives two
    /// minutes. An account banned or deactivated inside that window must not complete login with a
    /// valid code: no success, not online, not bound to the connection, nothing published.
    /// </summary>
    [Theory]
    [InlineData(AccountStatus.Banned, AuthResult.BANNED)]
    [InlineData(AccountStatus.Deactivated, AuthResult.DEACTIVATED)]
    public async Task Refuse_A_Valid_Code_For_An_Account_That_Stopped_Being_Active_After_The_Password_Step(
        AccountStatus status, AuthResult expected)
    {
        // Active when the password step issued the hash; the status changes before the code arrives.
        var account = MakeAccount();
        Assert.Equal(AccountStatus.Active, account.Status);
        var accountId = new AccountId(1L);
        _mfaService.VerifyMFAAsync("valid-hash", "123456").Returns(new MFAVerifyResult(true, accountId));
        _accountRepository.FindByIdAsync(accountId).Returns(account);
        account.Status = status;

        await CreateHandler().ExecuteAsync(new AuthPacketContext<CMFAVerifyPacket>
        {
            Packet = new CMFAVerifyPacket { MfaHash = "valid-hash", Code = "123456" },
            Connection = _connection
        });

        NetworkPacket sent = (NetworkPacket)_connection.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IAuthConnection.Send))
            .GetArguments()[0]!;
        // FakeAvalonCryptoSession.Encrypt is a pass-through, so the payload is the plain protobuf.
        using var stream = new MemoryStream(sent.Payload);
        SAuthResultPacket packet = Serializer.Deserialize<SAuthResultPacket>(stream);
        Assert.Equal(expected, packet.Result);
        Assert.Equal(0, packet.AccountId);

        Assert.False(account.Online);
        _connection.DidNotReceiveWithAnyArgs().AccountId = default;
        await _accountRepository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
        await _cache.DidNotReceiveWithAnyArgs().PublishAsync(default!, default!);
    }

    private SAuthResultPacket SentPacket()
    {
        NetworkPacket sent = (NetworkPacket)_connection.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IAuthConnection.Send))
            .GetArguments()[0]!;
        using var stream = new MemoryStream(sent.Payload);
        return Serializer.Deserialize<SAuthResultPacket>(stream);
    }

    /// <summary>
    /// #471: an account locked between the password step and the code (by failed logins inside the
    /// MFA hash's two minutes) must not finish logging in with a valid code.
    /// </summary>
    [Fact]
    public async Task Refuse_a_valid_code_for_an_account_locked_after_the_password_step()
    {
        var account = MakeAccount();
        var accountId = new AccountId(1L);
        _mfaService.VerifyMFAAsync("valid-hash", "123456").Returns(new MFAVerifyResult(true, accountId));
        _accountRepository.FindByIdAsync(accountId).Returns(account);
        account.Locked = true;
        account.LockedUntil = DateTime.UtcNow.AddMinutes(15);

        await CreateHandler().ExecuteAsync(new AuthPacketContext<CMFAVerifyPacket>
        {
            Packet = new CMFAVerifyPacket { MfaHash = "valid-hash", Code = "123456" },
            Connection = _connection
        });

        SAuthResultPacket packet = SentPacket();
        Assert.Equal(AuthResult.LOCKED, packet.Result);
        Assert.Equal(0, packet.AccountId);
        Assert.False(account.Online);
        _connection.DidNotReceiveWithAnyArgs().AccountId = default;
        await _accountRepository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
        await _cache.DidNotReceiveWithAnyArgs().PublishAsync(default!, default!);
    }

    // ── #471 review: wrong codes are counted ─────────────────────────────────

    private const string SourceKey = "auth:source:127.0.0.1:failedLogins";

    private Task VerifyAsync(string code = "000000") =>
        CreateHandler().ExecuteAsync(new AuthPacketContext<CMFAVerifyPacket>
        {
            Packet = new CMFAVerifyPacket { MfaHash = "valid-hash", Code = code },
            Connection = _connection
        });

    [Fact]
    public async Task Count_a_wrong_code_against_the_source()
    {
        _mfaService.VerifyMFAAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MFAVerifyResult(false, null));

        await VerifyAsync();

        Assert.Equal(AuthResult.MFA_FAILED, SentPacket().Result);
        await _cache.Received(1).IncrementAsync(SourceKey, Arg.Any<TimeSpan>());
        await _cache.DidNotReceiveWithAnyArgs().DecrementFloorAsync(default!);
    }

    [Fact]
    public async Task Refuse_a_source_past_its_limit_before_checking_the_code()
    {
        _cache.IncrementAsync(SourceKey, Arg.Any<TimeSpan>()).Returns(11L);

        await VerifyAsync();

        Assert.Equal(AuthResult.LOCKED, SentPacket().Result);
        await _mfaService.DidNotReceiveWithAnyArgs().VerifyMFAAsync(default!, default!, default);
    }

    [Fact]
    public async Task Count_a_wrong_code_against_the_mfa_hash()
    {
        var accountId = new AccountId(1L);
        _mfaHashService.GetAccountIdAsync("valid-hash").Returns(accountId);
        _mfaHashService.RecordAttemptAsync(accountId).Returns(1L);
        _mfaService.VerifyMFAAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MFAVerifyResult(false, null));

        await VerifyAsync();

        await _mfaHashService.Received(1).RecordAttemptAsync(accountId);
        await _mfaHashService.DidNotReceiveWithAnyArgs().CleanupHash(default!);
    }

    /// <summary>The fifth wrong code deletes the hash: the client has to log in again for another.</summary>
    [Fact]
    public async Task Delete_the_mfa_hash_after_the_last_allowed_wrong_code()
    {
        var accountId = new AccountId(1L);
        _mfaHashService.GetAccountIdAsync("valid-hash").Returns(accountId);
        _mfaHashService.RecordAttemptAsync(accountId).Returns(5L);
        _mfaService.VerifyMFAAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MFAVerifyResult(false, null));

        await VerifyAsync();

        Assert.Equal(AuthResult.MFA_FAILED, SentPacket().Result);
        await _mfaHashService.Received(1).CleanupHash("valid-hash");
    }

    [Fact]
    public async Task Refuse_a_code_past_the_attempt_limit_without_checking_it()
    {
        var accountId = new AccountId(1L);
        _mfaHashService.GetAccountIdAsync("valid-hash").Returns(accountId);
        _mfaHashService.RecordAttemptAsync(accountId).Returns(6L);
        _mfaService.VerifyMFAAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MFAVerifyResult(true, accountId));

        await VerifyAsync("123456");

        Assert.Equal(AuthResult.MFA_FAILED, SentPacket().Result);
        await _mfaService.DidNotReceiveWithAnyArgs().VerifyMFAAsync(default!, default!, default);
        await _mfaHashService.Received(1).CleanupHash("valid-hash");
    }

    /// <summary>
    /// Re-review: a fresh password login makes a fresh MFA hash, so the per-hash cap reset on every
    /// login, and a correct password gives its source slot back. A wrong code must therefore count
    /// towards the account lock like a wrong password does. The reply goes first, as for a password.
    /// </summary>
    [Fact]
    public async Task Count_a_wrong_code_towards_the_account_lock()
    {
        var accountId = new AccountId(7L);
        _mfaHashService.GetAccountIdAsync("valid-hash").Returns(accountId);
        _mfaService.VerifyMFAAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MFAVerifyResult(false, null));

        DateTime before = DateTime.UtcNow;
        await VerifyAsync();
        DateTime after = DateTime.UtcNow;

        Assert.Equal(AuthResult.MFA_FAILED, SentPacket().Result);
        await _accountRepository.Received(1).RecordFailedLoginAsync(accountId, "127.0.0.1", Arg.Any<DateTime>(),
            (DateTime?)null, Arg.Any<CancellationToken>());
        Received.InOrder(() =>
        {
            _connection.Send(Arg.Any<NetworkPacket>());
            _accountRepository.RecordFailedLoginAsync(accountId, Arg.Any<string>(), Arg.Any<DateTime>(),
                Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
        });
    }

    /// <summary>
    /// #484: codes spend the username's budget, and the wrong code in its last slot locks the
    /// account for the lockout duration and is answered LOCKED, as a wrong password there is.
    /// </summary>
    [Fact]
    public async Task Lock_the_account_on_the_wrong_code_in_the_username_budgets_last_slot()
    {
        var accountId = new AccountId(7L);
        _mfaHashService.GetAccountIdAsync("valid-hash").Returns(accountId);
        _mfaService.VerifyMFAAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MFAVerifyResult(false, null));
        string usernameKey = UsernameBudget.KeyFor("TESTUSER");
        _cache.IncrementAsync(usernameKey, Arg.Any<TimeSpan>()).Returns(5L);

        DateTime before = DateTime.UtcNow;
        await VerifyAsync();
        DateTime after = DateTime.UtcNow;

        Assert.Equal(AuthResult.LOCKED, SentPacket().Result);
        await _accountRepository.Received(1).RecordFailedLoginAsync(accountId, "127.0.0.1", Arg.Any<DateTime>(),
            Arg.Is<DateTime?>(d => d >= before.AddMinutes(15) && d <= after.AddMinutes(15)), Arg.Any<CancellationToken>());
        await _cache.Received(1).KeyExpireAsync(usernameKey, TimeSpan.FromMinutes(15));
    }

    [Fact]
    public async Task Not_count_a_correct_code_towards_the_account_lock()
    {
        var account = MakeAccount();
        var accountId = new AccountId(1L);
        _mfaService.VerifyMFAAsync("valid-hash", "123456").Returns(new MFAVerifyResult(true, accountId));
        _accountRepository.FindByIdAsync(accountId).Returns(account);

        await VerifyAsync("123456");

        await _accountRepository.DidNotReceiveWithAnyArgs().RecordFailedLoginAsync(default!, default!, default, default, default);
    }

    /// <summary>
    /// Re-review: between the :mfa hash being deleted and its reverse key being deleted (an expiry,
    /// or an admin removing MFA), the attempt count is -1. Such a request is refused, not checked.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Refuse_a_code_whose_mfa_hash_is_gone_without_checking_it(bool reverseKeyStillThere)
    {
        var accountId = new AccountId(1L);
        _mfaHashService.GetAccountIdAsync("valid-hash").Returns(reverseKeyStillThere ? accountId : null);
        _mfaHashService.RecordAttemptAsync(accountId).Returns(-1L);
        _mfaService.VerifyMFAAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MFAVerifyResult(true, accountId));

        await VerifyAsync("123456");

        Assert.Equal(AuthResult.MFA_FAILED, SentPacket().Result);
        await _mfaService.DidNotReceiveWithAnyArgs().VerifyMFAAsync(default!, default!, default);
        _connection.DidNotReceiveWithAnyArgs().AccountId = default;
    }

    [Fact]
    public async Task Give_back_its_source_slot_on_a_correct_code()
    {
        var account = MakeAccount();
        var accountId = new AccountId(1L);
        _mfaService.VerifyMFAAsync("valid-hash", "123456").Returns(new MFAVerifyResult(true, accountId));
        _accountRepository.FindByIdAsync(accountId).Returns(account);

        await VerifyAsync("123456");

        Assert.Equal(AuthResult.SUCCESS, SentPacket().Result);
        await _cache.Received(1).DecrementFloorAsync(SourceKey);
    }

    [Fact]
    public async Task Record_the_full_ipv6_address_as_the_last_login_address()
    {
        _connection.RemoteEndPoint.Returns("[2001:db8:1:2:3:4:5:6]:50000");
        var account = MakeAccount();
        var accountId = new AccountId(1L);
        _mfaService.VerifyMFAAsync("valid-hash", "123456").Returns(new MFAVerifyResult(true, accountId));
        _accountRepository.FindByIdAsync(accountId).Returns(account);

        await VerifyAsync("123456");

        await _accountRepository.Received(1).TryRecordLoginAsync(accountId, "2001:db8:1:2:3:4:5:6",
            Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Accept_a_valid_code_for_an_account_whose_lock_has_expired()
    {
        var account = MakeAccount();
        var accountId = new AccountId(1L);
        _mfaService.VerifyMFAAsync("valid-hash", "123456").Returns(new MFAVerifyResult(true, accountId));
        _accountRepository.FindByIdAsync(accountId).Returns(account);
        account.Locked = true;
        account.LockedUntil = DateTime.UtcNow.AddSeconds(-1);

        await CreateHandler().ExecuteAsync(new AuthPacketContext<CMFAVerifyPacket>
        {
            Packet = new CMFAVerifyPacket { MfaHash = "valid-hash", Code = "123456" },
            Connection = _connection
        });

        Assert.Equal(AuthResult.SUCCESS, SentPacket().Result);
        Assert.True(account.Online);
        Assert.False(account.Locked);
        Assert.Null(account.LockedUntil);
    }
}
