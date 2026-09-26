using System.Text;
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
using Avalon.Infrastructure.Login;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using ProtoBuf;

namespace Avalon.Server.Auth.UnitTests.Handlers;

public class CAuthHandlerShould
{
    private readonly IAccountRepository _accountRepository = Substitute.For<IAccountRepository>();
    private readonly IReplicatedCache _cache = Substitute.For<IReplicatedCache>();
    private readonly IMFAHashService _mfaHashService = Substitute.For<IMFAHashService>();
    private readonly IMfaSetupRepository _noMfaRepo = Substitute.For<IMfaSetupRepository>();
    private readonly IAuthConnection _connection = Substitute.For<IAuthConnection>();
    private readonly IAvalonCryptoSession _cryptoSession = new FakeAvalonCryptoSession();
    private readonly IPasswordVerifier _passwordVerifier = new BCryptPasswordVerifier();
    private readonly CAuthHandler _handler;

    private static IOptions<AuthConfiguration> AuthOptions(int maxFailedLogins = 5) =>
        Options.Create(new AuthConfiguration { MaxFailedLoginAttempts = maxFailedLogins });

    private CAuthHandler CreateHandler(int maxFailedLogins = 5) =>
        new(NullLoggerFactory.Instance, _accountRepository, _cache, _mfaHashService, _noMfaRepo, AuthOptions(maxFailedLogins), _passwordVerifier);

    public CAuthHandlerShould()
    {
        _connection.CryptoSession.Returns(_cryptoSession);
        _connection.RemoteEndPoint.Returns("127.0.0.1:12345");
        _connection.Id.Returns(Guid.NewGuid());
        _accountRepository.TryRecordLoginAsync(default!, default!, default, default, default).ReturnsForAnyArgs(true);
        _handler = CreateHandler();
    }

    private static Account MakeAccount(string username = "TESTUSER", bool locked = false, bool online = false, int failedLogins = 0)
    {
        var password = BCrypt.Net.BCrypt.HashPassword("correct_password");
        return new Account
        {
            Username = username,
            Salt = new byte[16],
            Verifier = Encoding.UTF8.GetBytes(password),
            Email = "test@test.com",
            JoinDate = DateTime.UtcNow,
            Locked = locked,
            Online = online,
            FailedLogins = failedLogins,
            Id = new AccountId(1L)
        };
    }

    [Fact]
    public async Task SendInvalidCredentials_WhenUsernameIsNull()
    {
        var ctx = new AuthPacketContext<CAuthPacket>
        {
            Packet = new CAuthPacket { Username = null!, Password = "abc" },
            Connection = _connection
        };

        await _handler.ExecuteAsync(ctx);

        _connection.Received(1).Send(Arg.Any<NetworkPacket>());
        await _accountRepository.DidNotReceive().FindByUserNameAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task SendInvalidCredentials_WhenPasswordIsWhitespace()
    {
        var ctx = new AuthPacketContext<CAuthPacket>
        {
            Packet = new CAuthPacket { Username = "user", Password = "   " },
            Connection = _connection
        };

        await _handler.ExecuteAsync(ctx);

        _connection.Received(1).Send(Arg.Any<NetworkPacket>());
        await _accountRepository.DidNotReceive().FindByUserNameAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task SendInvalidCredentials_WhenUsernameIsEmptyString()
    {
        var ctx = new AuthPacketContext<CAuthPacket>
        {
            Packet = new CAuthPacket { Username = "", Password = "pass" },
            Connection = _connection
        };

        await _handler.ExecuteAsync(ctx);

        _connection.Received(1).Send(Arg.Any<NetworkPacket>());
        await _accountRepository.DidNotReceive().FindByUserNameAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task LookupAccount_UsingUppercaseTrimmedUsername()
    {
        _accountRepository.FindByUserNameAsync("TESTUSER").Returns((Account?)null);

        var ctx = new AuthPacketContext<CAuthPacket>
        {
            Packet = new CAuthPacket { Username = "  testUser  ", Password = "pass" },
            Connection = _connection
        };

        await _handler.ExecuteAsync(ctx);

        await _accountRepository.Received(1).FindByUserNameAsync("TESTUSER");
    }

    [Fact]
    public async Task SendInvalidCredentials_WhenAccountNotFound()
    {
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns((Account?)null);

        var ctx = new AuthPacketContext<CAuthPacket>
        {
            Packet = new CAuthPacket { Username = "unknown", Password = "pass" },
            Connection = _connection
        };

        await _handler.ExecuteAsync(ctx);

        _connection.Received(1).Send(Arg.Any<NetworkPacket>());
        await _accountRepository.DidNotReceive().UpdateAsync(Arg.Any<Account>());
    }

    [Fact]
    public async Task SendLocked_WhenAccountIsLocked()
    {
        var account = MakeAccount(locked: true);
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(account);

        var ctx = new AuthPacketContext<CAuthPacket>
        {
            Packet = new CAuthPacket { Username = "testuser", Password = "pass" },
            Connection = _connection
        };

        await _handler.ExecuteAsync(ctx);

        _connection.Received(1).Send(Arg.Any<NetworkPacket>());
        await _accountRepository.DidNotReceive().UpdateAsync(Arg.Any<Account>());
    }

    [Fact]
    public async Task SendInvalidCredentials_WhenPasswordIsWrong_AndIncrementFailedLogins()
    {
        var account = MakeAccount(failedLogins: 0);
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(account);

        var ctx = new AuthPacketContext<CAuthPacket>
        {
            Packet = new CAuthPacket { Username = "testuser", Password = "wrong_password" },
            Connection = _connection
        };

        await _handler.ExecuteAsync(ctx);

        Assert.Equal(AuthResult.INVALID_CREDENTIALS, SentResult());
        _connection.Received(1).Send(Arg.Any<NetworkPacket>());
        await _accountRepository.Received(1).RecordFailedLoginAsync(account.Id, "127.0.0.1", Arg.Any<DateTime>(),
            (DateTime?)null, Arg.Any<CancellationToken>());
        await _accountRepository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task SendLocked_WhenFailedLoginAttemptsReachDefaultThreshold()
    {
        var account = MakeAccount();
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(account);
        _cache.IncrementAsync(UsernameKey, Arg.Any<TimeSpan>()).Returns(5L); // the default threshold of 5

        var ctx = new AuthPacketContext<CAuthPacket>
        {
            Packet = new CAuthPacket { Username = "testuser", Password = "wrong_password" },
            Connection = _connection
        };

        await _handler.ExecuteAsync(ctx);

        Assert.Equal(AuthResult.LOCKED, SentResult());
        await _accountRepository.Received(1).RecordFailedLoginAsync(account.Id, Arg.Any<string>(), Arg.Any<DateTime>(),
            Arg.Is<DateTime?>(d => d != null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LockAccount_WhenFailedLoginsReachConfiguredThreshold()
    {
        var account = MakeAccount();
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(account);
        _cache.IncrementAsync(UsernameKey, Arg.Any<TimeSpan>()).Returns(3L); // the threshold of 3
        var handler = CreateHandler(maxFailedLogins: 3);

        var ctx = new AuthPacketContext<CAuthPacket>
        {
            Packet = new CAuthPacket { Username = "testuser", Password = "wrong_password" },
            Connection = _connection
        };

        await handler.ExecuteAsync(ctx);

        Assert.Equal(AuthResult.LOCKED, SentResult());
        await _accountRepository.Received(1).RecordFailedLoginAsync(account.Id, Arg.Any<string>(), Arg.Any<DateTime>(),
            Arg.Is<DateTime?>(d => d != null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotLockAccount_WhenFailedLoginsBelowConfiguredThreshold()
    {
        // The row's own count does not decide the lock (#484): only the username budget does.
        var account = MakeAccount(failedLogins: 40);
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(account);
        _cache.IncrementAsync(UsernameKey, Arg.Any<TimeSpan>()).Returns(5L); // 5 failures, but threshold is 10
        var handler = CreateHandler(maxFailedLogins: 10);

        var ctx = new AuthPacketContext<CAuthPacket>
        {
            Packet = new CAuthPacket { Username = "testuser", Password = "wrong_password" },
            Connection = _connection
        };

        await handler.ExecuteAsync(ctx);

        Assert.Equal(AuthResult.INVALID_CREDENTIALS, SentResult());
        await _accountRepository.Received(1).RecordFailedLoginAsync(account.Id, Arg.Any<string>(), Arg.Any<DateTime>(),
            (DateTime?)null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResetFailedLogins_OnSuccessfulLogin_RegardlessOfThreshold()
    {
        var account = MakeAccount(failedLogins: 3);
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(account);
        var handler = CreateHandler(maxFailedLogins: 10);

        var ctx = new AuthPacketContext<CAuthPacket>
        {
            Packet = new CAuthPacket { Username = "testuser", Password = "correct_password" },
            Connection = _connection
        };

        await handler.ExecuteAsync(ctx);

        Assert.Equal(0, account.FailedLogins);
        Assert.True(account.Online);
    }

    [Fact]
    public async Task SendAlreadyConnected_WhenAccountIsOnline_AndNoSessionFound()
    {
        var account = MakeAccount(online: true);
        account.Id = new AccountId(42L);
        Guid staleSession = Guid.NewGuid();
        account.OnlineSessionId = staleSession;
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(account);

        // Server.Connections returns empty — no connected session found
        var hostingOptions = Substitute.For<IOptions<HostingConfiguration>>();
        hostingOptions.Value.Returns(new HostingConfiguration { Port = 0, Host = "127.0.0.1" });
        var securityOptions = Substitute.For<IOptions<HostingSecurity>>();
        securityOptions.Value.Returns(new HostingSecurity());
        var server = new AuthServer(
            Substitute.For<IServiceProvider>(),
            Substitute.For<IPacketManager>(),
            NullLoggerFactory.Instance,
            Substitute.For<IAccountRepository>(),
            Substitute.For<IReplicatedCache>(),
            hostingOptions,
            securityOptions);
        _connection.Server.Returns(server);

        var ctx = new AuthPacketContext<CAuthPacket>
        {
            Packet = new CAuthPacket { Username = "testuser", Password = "correct_password" },
            Connection = _connection
        };

        await _handler.ExecuteAsync(ctx);

        _connection.Received(1).Send(Arg.Any<NetworkPacket>());
        await _cache.Received(1).PublishAsync("world:accounts:disconnect", Arg.Any<string>());
        // No session found => only the Online flag is cleared, never the whole row (#484)
        Assert.False(account.Online);
        // ...and only while the session that set it is still the one online (#487).
        // Its own publish is noted, so the login that follows is not kicked by it (#495 review).
        Assert.NotNull(server.OwnDisconnectPublishedAt(account.Id, System.Diagnostics.Stopwatch.GetTimestamp()));
        await _accountRepository.Received(1).MarkOfflineAsync(account.Id, staleSession, 0, Arg.Any<CancellationToken>());
        await _accountRepository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task SendSuccess_AndSetAccountOnline_WhenCredentialsAreValid()
    {
        var account = MakeAccount();
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(account);

        var ctx = new AuthPacketContext<CAuthPacket>
        {
            Packet = new CAuthPacket { Username = "testuser", Password = "correct_password" },
            Connection = _connection
        };

        await _handler.ExecuteAsync(ctx);

        Assert.True(account.Online);
        Assert.Equal(0, account.FailedLogins);
        _connection.Received(1).Send(Arg.Any<NetworkPacket>());
        // The login is recorded as this connection's session (#487).
        await _accountRepository.Received(1).TryRecordLoginAsync(account.Id, "127.0.0.1", Arg.Any<DateTime>(),
            _connection.Id, Arg.Any<CancellationToken>());
        await _accountRepository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
        await _cache.Received(1).PublishAsync("auth:accounts:online", Arg.Any<string>());
    }

    [Fact]
    public async Task SetConnectionAccountId_WhenLoginSucceeds()
    {
        var account = MakeAccount();
        account.Id = new AccountId(99L);
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(account);

        var ctx = new AuthPacketContext<CAuthPacket>
        {
            Packet = new CAuthPacket { Username = "testuser", Password = "correct_password" },
            Connection = _connection
        };

        await _handler.ExecuteAsync(ctx);

        _connection.Received().AccountId = account.Id;
    }

    /// <summary>#495: the connection keeps the version of the row its password was checked against.</summary>
    [Fact]
    public async Task SetConnectionCredentialsVersion_FromTheRowThePasswordMatched()
    {
        var account = MakeAccount();
        account.CredentialsVersion = 7;
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(account);

        await LogInAsync(_handler);

        _connection.Received().CredentialsVersion = 7;
    }

    [Fact]
    public async Task SendMfaRequired_WhenAccountHasConfirmedMfa()
    {
        var account = MakeAccount();
        var mfaSetup = new MFASetup { Status = MfaSetupStatus.Confirmed };
        var mfaSetupRepo = Substitute.For<IMfaSetupRepository>();
        mfaSetupRepo.FindByAccountIdAsync(Arg.Any<AccountId>()).Returns(mfaSetup);
        _mfaHashService.GenerateHashAsync(Arg.Any<Account>()).Returns("test-hash");
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(account);

        var handler = new CAuthHandler(NullLoggerFactory.Instance, _accountRepository, _cache, _mfaHashService, mfaSetupRepo, AuthOptions(), _passwordVerifier);
        var ctx = new AuthPacketContext<CAuthPacket>
        {
            Packet = new CAuthPacket { Username = "testuser", Password = "correct_password" },
            Connection = _connection
        };

        await handler.ExecuteAsync(ctx);

        _connection.Received(1).Send(Arg.Any<NetworkPacket>());
        await _mfaHashService.Received(1).GenerateHashAsync(account);
        await _accountRepository.DidNotReceive().UpdateAsync(Arg.Any<Account>());
    }

    [Fact]
    public async Task SendSuccess_WhenAccountHasNoMfa()
    {
        var account = MakeAccount();
        var mfaSetupRepo = Substitute.For<IMfaSetupRepository>();
        mfaSetupRepo.FindByAccountIdAsync(Arg.Any<AccountId>()).Returns((MFASetup?)null);
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(account);

        var handler = new CAuthHandler(NullLoggerFactory.Instance, _accountRepository, _cache, _mfaHashService, mfaSetupRepo, AuthOptions(), _passwordVerifier);
        var ctx = new AuthPacketContext<CAuthPacket>
        {
            Packet = new CAuthPacket { Username = "testuser", Password = "correct_password" },
            Connection = _connection
        };

        await handler.ExecuteAsync(ctx);

        Assert.True(account.Online);
        _connection.Received(1).Send(Arg.Any<NetworkPacket>());
    }

    private AuthResult? SentResult()
    {
        NetworkPacket? sent = _connection.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IAuthConnection.Send))
            .Select(c => c.GetArguments()[0])
            .OfType<NetworkPacket>()
            .LastOrDefault();
        if (sent == null) return null;

        // FakeAvalonCryptoSession.Encrypt is a pass-through, so the payload is the plain protobuf.
        using var stream = new MemoryStream(sent.Payload);
        return Serializer.Deserialize<SAuthResultPacket>(stream).Result;
    }

    /// <summary>
    /// #462: the TCP login verified the password and the lock flag but never looked at Status, so a
    /// banned or deactivated account logged in, listed worlds and was issued a world key.
    /// </summary>
    [Theory]
    [InlineData(AccountStatus.Banned, false)]
    [InlineData(AccountStatus.Banned, true)]
    [InlineData(AccountStatus.Deactivated, false)]
    [InlineData(AccountStatus.Deactivated, true)]
    public async Task Refuse_A_Correct_Password_For_An_Account_That_Is_Not_Active(AccountStatus status, bool mfaConfirmed)
    {
        var account = MakeAccount();
        account.Status = status;
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(account);
        if (mfaConfirmed)
        {
            _noMfaRepo.FindByAccountIdAsync(Arg.Any<AccountId>())
                .Returns(new MFASetup { Status = MfaSetupStatus.Confirmed });
        }

        await _handler.ExecuteAsync(new AuthPacketContext<CAuthPacket>
        {
            Packet = new CAuthPacket { Username = "testuser", Password = "correct_password" },
            Connection = _connection
        });

        _connection.Received(1).Send(Arg.Any<NetworkPacket>());
        AuthResult? result = SentResult();
        Assert.NotEqual(AuthResult.SUCCESS, result);
        Assert.NotEqual(AuthResult.MFA_REQUIRED, result);
        Assert.Equal(status == AccountStatus.Banned ? AuthResult.BANNED : AuthResult.DEACTIVATED, result);

        // Stopped before MFA: the setup is never looked up and no MFA hash is issued.
        await _noMfaRepo.DidNotReceiveWithAnyArgs().FindByAccountIdAsync(default!, default);
        await _mfaHashService.DidNotReceiveWithAnyArgs().GenerateHashAsync(default!);

        // And before any success: not online, not bound to the connection, nothing published.
        Assert.False(account.Online);
        _connection.DidNotReceiveWithAnyArgs().AccountId = default;
        await _accountRepository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
        await _cache.DidNotReceiveWithAnyArgs().PublishAsync(default!, default!);
    }

    /// <summary>
    /// The status check sits after the password check, so a wrong password gets the same answer
    /// for a banned account as for an active one and cannot be used to probe for a ban.
    /// </summary>
    [Theory]
    [InlineData(AccountStatus.Banned)]
    [InlineData(AccountStatus.Deactivated)]
    public async Task Answer_A_Wrong_Password_For_An_Inactive_Account_As_Invalid_Credentials(AccountStatus status)
    {
        var account = MakeAccount();
        account.Status = status;
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(account);

        await _handler.ExecuteAsync(new AuthPacketContext<CAuthPacket>
        {
            Packet = new CAuthPacket { Username = "testuser", Password = "wrong_password" },
            Connection = _connection
        });

        Assert.Equal(AuthResult.INVALID_CREDENTIALS, SentResult());
        await _accountRepository.Received(1).RecordFailedLoginAsync(account.Id, Arg.Any<string>(), Arg.Any<DateTime>(),
            Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    // ── #471: login hardening ─────────────────────────────────────────────────

    private const string SourceKey = "auth:source:127.0.0.1:failedLogins";

    private static readonly string UsernameKey = UsernameBudget.KeyFor("testuser");

    private static IOptions<AuthConfiguration> HardeningOptions(int lockoutMinutes = 15, int perSource = 10,
        int sourceWindowMinutes = 15) =>
        Options.Create(new AuthConfiguration
        {
            MaxFailedLoginAttempts = 5,
            LockoutDurationMinutes = lockoutMinutes,
            MaxFailedLoginsPerSource = perSource,
            FailedLoginSourceWindowMinutes = sourceWindowMinutes,
        });

    private CAuthHandler CreateHandler(IOptions<AuthConfiguration> options, IPasswordVerifier? verifier = null) =>
        new(NullLoggerFactory.Instance, _accountRepository, _cache, _mfaHashService, _noMfaRepo, options,
            verifier ?? _passwordVerifier);

    private Task LogInAsync(CAuthHandler handler, string username = "testuser", string password = "correct_password") =>
        handler.ExecuteAsync(new AuthPacketContext<CAuthPacket>
        {
            Packet = new CAuthPacket { Username = username, Password = password },
            Connection = _connection
        });

    /// <summary>
    /// An unknown username answered before any BCrypt work, so it came back measurably faster than
    /// a wrong password on a real account. It must pay for one verify, against the fixed hash.
    /// </summary>
    [Fact]
    public async Task Run_a_bcrypt_verify_against_a_fixed_hash_when_the_username_is_unknown()
    {
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns((Account?)null);
        var verifier = Substitute.For<IPasswordVerifier>();

        await LogInAsync(CreateHandler(HardeningOptions(), verifier), "nobody", " some_password ");

        verifier.Received(1).Verify("some_password", BCryptPasswordVerifier.UnknownAccountHash);
        Assert.Equal(AuthResult.INVALID_CREDENTIALS, SentResult());
    }

    /// <summary>
    /// Owner decision (#487 re-review): a name outside the username rule is answered exactly like
    /// an unknown username (one verify against the fixed hash, INVALID_CREDENTIALS), even if a row
    /// holds its normalised form, and the lookup still runs, so it takes as long.
    /// </summary>
    [Theory]
    [InlineData("ab")]
    [InlineData("abcdefghijklmnopq")]
    [InlineData("test-user")]
    [InlineData("test user")]
    [InlineData(" testuser")]
    public async Task Answer_a_name_outside_the_rule_as_an_unknown_username(string username)
    {
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(MakeAccount());
        var verifier = Substitute.For<IPasswordVerifier>();

        await LogInAsync(CreateHandler(HardeningOptions(), verifier), username, "correct_password");

        verifier.Received(1).Verify("correct_password", BCryptPasswordVerifier.UnknownAccountHash);
        verifier.ReceivedWithAnyArgs(1).Verify(default!, default!);
        Assert.Equal(AuthResult.INVALID_CREDENTIALS, SentResult());
        await _accountRepository.ReceivedWithAnyArgs(1).FindByUserNameAsync(default!, default);
        await _accountRepository.DidNotReceiveWithAnyArgs().TryRecordLoginAsync(default!, default!, default, default, default);
    }

    /// <summary>
    /// #478 review: a locked row answered LOCKED before any BCrypt work, so with its budget hold
    /// gone (expired, or Redis lost it) it answered faster than an unknown username. It pays for one
    /// verify against the fixed hash too, and the account's own hash is still never checked.
    /// </summary>
    [Fact]
    public async Task Run_a_bcrypt_verify_against_the_fixed_hash_for_a_locked_account()
    {
        var account = MakeAccount(locked: true);
        account.LockedUntil = DateTime.UtcNow.AddMinutes(10);
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(account);
        var verifier = Substitute.For<IPasswordVerifier>();

        await LogInAsync(CreateHandler(HardeningOptions(), verifier), password: "correct_password");

        verifier.Received(1).Verify("correct_password", BCryptPasswordVerifier.UnknownAccountHash);
        verifier.ReceivedWithAnyArgs(1).Verify(default!, default!);
        Assert.Equal(AuthResult.LOCKED, SentResult());
    }

    [Fact]
    public void Make_the_unknown_account_hash_a_real_bcrypt_hash_at_the_registration_work_factor()
    {
        string hash = BCryptPasswordVerifier.UnknownAccountHash;

        // Same algorithm and cost as a registered account's hash, so the verify costs the same.
        Assert.Equal(BCrypt.Net.BCrypt.HashPassword("x", BCrypt.Net.BCrypt.GenerateSalt())[..7], hash[..7]);
        Assert.False(new BCryptPasswordVerifier().Verify("correct_password", hash));
    }

    [Fact]
    public async Task Refuse_a_locked_account_until_its_lock_expires()
    {
        var account = MakeAccount(locked: true, failedLogins: 5);
        account.LockedUntil = DateTime.UtcNow.AddMinutes(5);
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(account);

        await LogInAsync(CreateHandler(HardeningOptions()));

        Assert.Equal(AuthResult.LOCKED, SentResult());
        Assert.True(account.Locked);
        Assert.False(account.Online);
    }

    [Fact]
    public async Task Unlock_an_account_whose_lock_has_expired()
    {
        var account = MakeAccount(locked: true, failedLogins: 5);
        account.LockedUntil = DateTime.UtcNow.AddSeconds(-1);
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(account);

        await LogInAsync(CreateHandler(HardeningOptions()));

        Assert.Equal(AuthResult.SUCCESS, SentResult());
        Assert.False(account.Locked);
        Assert.Null(account.LockedUntil);
        Assert.Equal(0, account.FailedLogins);
        Assert.True(account.Online);
    }

    /// <summary>
    /// The count starts again once a lock expires: one wrong password after it is one failure, not
    /// the failure that re-locks the account straight away.
    /// </summary>
    [Fact]
    public async Task Count_failed_logins_from_zero_after_a_lock_expires()
    {
        var account = MakeAccount(locked: true, failedLogins: 5);
        account.LockedUntil = DateTime.UtcNow.AddSeconds(-1);
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(account);

        await LogInAsync(CreateHandler(HardeningOptions()), password: "wrong_password");

        // Five failures on the expired lock would re-lock at once if they still counted.
        Assert.Equal(AuthResult.INVALID_CREDENTIALS, SentResult());
        await _accountRepository.Received(1).RecordFailedLoginAsync(account.Id, Arg.Any<string>(), Arg.Any<DateTime>(),
            (DateTime?)null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Lock_an_account_for_the_configured_duration_when_it_reaches_the_threshold()
    {
        var account = MakeAccount();
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(account);
        _cache.IncrementAsync(UsernameKey, Arg.Any<TimeSpan>()).Returns(5L);

        DateTime before = DateTime.UtcNow;
        await LogInAsync(CreateHandler(HardeningOptions(lockoutMinutes: 30)), password: "wrong_password");
        DateTime after = DateTime.UtcNow;

        Assert.Equal(AuthResult.LOCKED, SentResult());
        await _accountRepository.Received(1).RecordFailedLoginAsync(account.Id, Arg.Any<string>(), Arg.Any<DateTime>(),
            Arg.Is<DateTime?>(d => d >= before.AddMinutes(30) && d <= after.AddMinutes(30)), Arg.Any<CancellationToken>());
        await _cache.Received(1).HoldCounterAtLeastAsync(UsernameKey, 6, TimeSpan.FromMinutes(30));
    }

    /// <summary>A lock with no end (one set before locks expired, or by hand) is not lifted.</summary>
    [Fact]
    public async Task Keep_refusing_a_locked_account_whose_lock_has_no_end()
    {
        var account = MakeAccount(locked: true);
        account.LockedUntil = null;
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(account);

        await LogInAsync(CreateHandler(HardeningOptions()));

        Assert.Equal(AuthResult.LOCKED, SentResult());
        Assert.True(account.Locked);
    }

    /// <summary>
    /// Anyone could lock any account by failing its password. A source past its limit is refused
    /// before the account is looked up, so it can neither guess nor push an account into a lock.
    /// </summary>
    [Fact]
    public async Task Refuse_a_source_past_its_failed_login_limit_before_touching_any_account()
    {
        _cache.IncrementAsync(SourceKey, Arg.Any<TimeSpan>()).Returns(11L);
        var account = MakeAccount();
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(account);
        var verifier = Substitute.For<IPasswordVerifier>();

        await LogInAsync(CreateHandler(HardeningOptions(perSource: 10), verifier), password: "wrong_password");

        Assert.Equal(AuthResult.LOCKED, SentResult());
        await _accountRepository.DidNotReceiveWithAnyArgs().FindByUserNameAsync(default!, default);
        verifier.DidNotReceiveWithAnyArgs().Verify(default!, default!);
        await _accountRepository.DidNotReceiveWithAnyArgs().RecordFailedLoginAsync(default!, default!, default, default, default);
    }

    [Fact]
    public async Task Allow_a_source_below_its_failed_login_limit()
    {
        // The tenth attempt in the window: it takes the last slot.
        _cache.IncrementAsync(SourceKey, Arg.Any<TimeSpan>()).Returns(10L);
        var account = MakeAccount();
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(account);

        await LogInAsync(CreateHandler(HardeningOptions(perSource: 10)));

        Assert.Equal(AuthResult.SUCCESS, SentResult());
    }

    /// <summary>The counter expires with its window; once it has, the source is allowed again.</summary>
    [Fact]
    public async Task Allow_a_source_again_once_its_window_has_passed()
    {
        // The counter expires with its window, so the next increment starts it again at one.
        _cache.IncrementAsync(SourceKey, Arg.Any<TimeSpan>()).Returns(11L, 1L);
        var account = MakeAccount();
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(account);
        CAuthHandler handler = CreateHandler(HardeningOptions(perSource: 10));

        await LogInAsync(handler);
        Assert.Equal(AuthResult.LOCKED, SentResult());
        Assert.False(account.Online);

        await LogInAsync(handler);
        Assert.Equal(AuthResult.SUCCESS, SentResult());
        Assert.True(account.Online);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Count_a_failed_login_against_its_source_for_the_configured_window(bool knownUsername)
    {
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(knownUsername ? MakeAccount() : null);

        await LogInAsync(CreateHandler(HardeningOptions(sourceWindowMinutes: 20)), password: "wrong_password");

        Assert.Equal(AuthResult.INVALID_CREDENTIALS, SentResult());
        await _cache.Received(1).IncrementAsync(SourceKey, TimeSpan.FromMinutes(20));
    }

    // ── #471 review ───────────────────────────────────────────────────────────

    /// <summary>
    /// Checking the counter, then verifying, then counting let every connection that arrived before
    /// the first failure was counted through. Each request must take its slot before it is let in.
    /// </summary>
    [Fact]
    public async Task Let_no_more_than_the_source_limit_through_when_failures_arrive_in_parallel()
    {
        long counter = 0;
        _cache.IncrementAsync(SourceKey, Arg.Any<TimeSpan>()).Returns(_ => Interlocked.Increment(ref counter));
        _cache.GetAsync(SourceKey).Returns(_ => Interlocked.Read(ref counter).ToString());
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _accountRepository.FindByUserNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => gate.Task.ContinueWith(_ => (Account?)MakeAccount(), TaskScheduler.Default));
        int verifies = 0;
        var verifier = Substitute.For<IPasswordVerifier>();
        verifier.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(_ =>
        {
            Interlocked.Increment(ref verifies);
            return false;
        });
        CAuthHandler handler = CreateHandler(HardeningOptions(perSource: 10), verifier);

        // Every request starts before any is let past the lookup, the shape of parallel connections.
        Task[] logins = Enumerable.Range(0, 40)
            .Select(_ => LogInAsync(handler, password: "wrong_password"))
            .ToArray();
        gate.SetResult();
        await Task.WhenAll(logins);

        Assert.InRange(verifies, 1, 10);
    }

    [Fact]
    public async Task Refuse_on_the_count_the_increment_returns()
    {
        _cache.IncrementAsync(SourceKey, Arg.Any<TimeSpan>()).Returns(11L);
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(MakeAccount());

        await LogInAsync(CreateHandler(HardeningOptions(perSource: 10)));

        Assert.Equal(AuthResult.LOCKED, SentResult());
        await _accountRepository.DidNotReceiveWithAnyArgs().FindByUserNameAsync(default!, default);
    }

    /// <summary>A correct password gives back its own slot, and only its own.</summary>
    [Fact]
    public async Task Give_back_its_own_source_slot_on_a_correct_password()
    {
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(MakeAccount());

        await LogInAsync(CreateHandler(HardeningOptions()));

        await _cache.Received(1).DecrementFloorAsync(SourceKey);
        await _cache.DidNotReceive().RemoveAsync(SourceKey);
    }

    [Fact]
    public async Task Keep_its_source_slot_on_a_wrong_password()
    {
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(MakeAccount());

        await LogInAsync(CreateHandler(HardeningOptions()), password: "wrong_password");

        await _cache.DidNotReceiveWithAnyArgs().DecrementFloorAsync(default!);
    }

    /// <summary>Probing for locked accounts costs budget like any other failed attempt.</summary>
    [Fact]
    public async Task Count_an_attempt_on_a_locked_account_against_its_source()
    {
        var account = MakeAccount(locked: true);
        account.LockedUntil = DateTime.UtcNow.AddMinutes(10);
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(account);

        await LogInAsync(CreateHandler(HardeningOptions()));

        Assert.Equal(AuthResult.LOCKED, SentResult());
        await _cache.Received(1).IncrementAsync(SourceKey, Arg.Any<TimeSpan>());
        await _cache.DidNotReceiveWithAnyArgs().DecrementFloorAsync(default!);
    }

    /// <summary>
    /// A wrong password on a known account wrote to the database before replying, and an unknown
    /// username did not, so the reply time told them apart. The reply now goes first.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reply_to_a_wrong_password_before_writing_the_failure(bool expiredLock)
    {
        var account = MakeAccount(locked: expiredLock, failedLogins: expiredLock ? 5 : 0);
        if (expiredLock) account.LockedUntil = DateTime.UtcNow.AddSeconds(-1);
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(account);

        await LogInAsync(CreateHandler(HardeningOptions()), password: "wrong_password");

        Assert.Equal(AuthResult.INVALID_CREDENTIALS, SentResult());
        Received.InOrder(() =>
        {
            _connection.Send(Arg.Any<NetworkPacket>());
            _accountRepository.RecordFailedLoginAsync(account.Id, Arg.Any<string>(), Arg.Any<DateTime>(),
                Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
        });
        await _accountRepository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    /// <summary>
    /// #484: the answer and the lock came from the row as read, which parallel requests all read
    /// before any of them wrote. They now come from the username budget's count alone.
    /// </summary>
    [Fact]
    public async Task Answer_and_lock_from_the_username_budget_not_from_the_row_as_read()
    {
        var account = MakeAccount(failedLogins: 4);
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(account);
        _cache.IncrementAsync(UsernameKey, Arg.Any<TimeSpan>()).Returns(1L);

        await LogInAsync(CreateHandler(HardeningOptions()), password: "wrong_password");

        Assert.Equal(AuthResult.INVALID_CREDENTIALS, SentResult());
        await _accountRepository.Received(1).RecordFailedLoginAsync(account.Id, "127.0.0.1", Arg.Any<DateTime>(),
            (DateTime?)null, Arg.Any<CancellationToken>());
        await _cache.DidNotReceiveWithAnyArgs().HoldCounterAtLeastAsync(default!, default, default);
    }

    [Theory]
    [InlineData("[2001:db8:1:2:3:4:5:6]:50000", "auth:source:2001:db8:1:2::/64:failedLogins", "2001:db8:1:2:3:4:5:6")]
    [InlineData("[::ffff:10.0.0.7]:50000", "auth:source:10.0.0.7:failedLogins", "10.0.0.7")]
    [InlineData("10.0.0.7:50000", "auth:source:10.0.0.7:failedLogins", "10.0.0.7")]
    public async Task Count_the_source_by_address_and_record_the_full_address(string endPoint, string sourceKey, string address)
    {
        _connection.RemoteEndPoint.Returns(endPoint);
        var account = MakeAccount();
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(account);

        await LogInAsync(CreateHandler(HardeningOptions()), password: "wrong_password");

        await _cache.Received(1).IncrementAsync(sourceKey, Arg.Any<TimeSpan>());
        await _accountRepository.Received(1).RecordFailedLoginAsync(account.Id, address, Arg.Any<DateTime>(),
            Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Record_the_full_ipv6_address_as_the_last_login_address()
    {
        _connection.RemoteEndPoint.Returns("[2001:db8:1:2:3:4:5:6]:50000");
        var account = MakeAccount();
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(account);

        await LogInAsync(CreateHandler(HardeningOptions()));

        await _accountRepository.Received(1).TryRecordLoginAsync(account.Id, "2001:db8:1:2:3:4:5:6",
            Arg.Any<DateTime>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Not_count_a_successful_login_against_its_source()
    {
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(MakeAccount());

        await LogInAsync(CreateHandler(HardeningOptions()));

        // The slot the attempt took is given back, so the success leaves the count where it was.
        Assert.Equal(AuthResult.SUCCESS, SentResult());
        await _cache.Received(1).IncrementAsync(SourceKey, Arg.Any<TimeSpan>());
        await _cache.Received(1).DecrementFloorAsync(SourceKey);
    }
}
