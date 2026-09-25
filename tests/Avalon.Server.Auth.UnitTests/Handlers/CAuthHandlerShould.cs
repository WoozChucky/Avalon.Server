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
using Avalon.Server.Auth.Services;
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

        Assert.Equal(1, account.FailedLogins);
        Assert.False(account.Locked);
        _connection.Received(1).Send(Arg.Any<NetworkPacket>());
        await _accountRepository.Received(1).UpdateAsync(account);
    }

    [Fact]
    public async Task SendLocked_WhenFailedLoginAttemptsReachDefaultThreshold()
    {
        var account = MakeAccount(failedLogins: 4); // one more will hit the default threshold of 5
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(account);

        var ctx = new AuthPacketContext<CAuthPacket>
        {
            Packet = new CAuthPacket { Username = "testuser", Password = "wrong_password" },
            Connection = _connection
        };

        await _handler.ExecuteAsync(ctx);

        Assert.Equal(5, account.FailedLogins);
        Assert.True(account.Locked);
        await _accountRepository.Received(1).UpdateAsync(account);
    }

    [Fact]
    public async Task LockAccount_WhenFailedLoginsReachConfiguredThreshold()
    {
        var account = MakeAccount(failedLogins: 2); // one more will hit threshold of 3
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(account);
        var handler = CreateHandler(maxFailedLogins: 3);

        var ctx = new AuthPacketContext<CAuthPacket>
        {
            Packet = new CAuthPacket { Username = "testuser", Password = "wrong_password" },
            Connection = _connection
        };

        await handler.ExecuteAsync(ctx);

        Assert.Equal(3, account.FailedLogins);
        Assert.True(account.Locked);
    }

    [Fact]
    public async Task NotLockAccount_WhenFailedLoginsBelowConfiguredThreshold()
    {
        var account = MakeAccount(failedLogins: 4); // 5 failures total, but threshold is 10
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(account);
        var handler = CreateHandler(maxFailedLogins: 10);

        var ctx = new AuthPacketContext<CAuthPacket>
        {
            Packet = new CAuthPacket { Username = "testuser", Password = "wrong_password" },
            Connection = _connection
        };

        await handler.ExecuteAsync(ctx);

        Assert.Equal(5, account.FailedLogins);
        Assert.False(account.Locked);
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
        // No session found => account.Online = false, UpdateAsync called
        Assert.False(account.Online);
        await _accountRepository.Received(1).UpdateAsync(account);
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
        await _accountRepository.Received(1).UpdateAsync(account);
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
        Assert.Equal(1, account.FailedLogins);
    }

    // ── #471: login hardening ─────────────────────────────────────────────────

    private const string SourceKey = "auth:source:127.0.0.1:failedLogins";

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

        Assert.Equal(AuthResult.INVALID_CREDENTIALS, SentResult());
        Assert.Equal(1, account.FailedLogins);
        Assert.False(account.Locked);
    }

    [Fact]
    public async Task Lock_an_account_for_the_configured_duration_when_it_reaches_the_threshold()
    {
        var account = MakeAccount(failedLogins: 4);
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(account);

        DateTime before = DateTime.UtcNow;
        await LogInAsync(CreateHandler(HardeningOptions(lockoutMinutes: 30)), password: "wrong_password");
        DateTime after = DateTime.UtcNow;

        Assert.Equal(AuthResult.LOCKED, SentResult());
        Assert.True(account.Locked);
        Assert.NotNull(account.LockedUntil);
        Assert.InRange(account.LockedUntil!.Value, before.AddMinutes(30), after.AddMinutes(30));
        await _accountRepository.Received(1).UpdateAsync(account);
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
        _cache.GetAsync(SourceKey).Returns("10");
        var account = MakeAccount();
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(account);
        var verifier = Substitute.For<IPasswordVerifier>();

        await LogInAsync(CreateHandler(HardeningOptions(perSource: 10), verifier), password: "wrong_password");

        Assert.Equal(AuthResult.LOCKED, SentResult());
        await _accountRepository.DidNotReceiveWithAnyArgs().FindByUserNameAsync(default!, default);
        verifier.DidNotReceiveWithAnyArgs().Verify(default!, default!);
        await _accountRepository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
        Assert.Equal(0, account.FailedLogins);
    }

    [Fact]
    public async Task Allow_a_source_below_its_failed_login_limit()
    {
        _cache.GetAsync(SourceKey).Returns("9");
        var account = MakeAccount();
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(account);

        await LogInAsync(CreateHandler(HardeningOptions(perSource: 10)));

        Assert.Equal(AuthResult.SUCCESS, SentResult());
    }

    /// <summary>The counter expires with its window; once it has, the source is allowed again.</summary>
    [Fact]
    public async Task Allow_a_source_again_once_its_window_has_passed()
    {
        _cache.GetAsync(SourceKey).Returns("10", (string?)null);
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

    [Fact]
    public async Task Not_count_a_successful_login_against_its_source()
    {
        _accountRepository.FindByUserNameAsync(Arg.Any<string>()).Returns(MakeAccount());

        await LogInAsync(CreateHandler(HardeningOptions()));

        Assert.Equal(AuthResult.SUCCESS, SentResult());
        await _cache.DidNotReceiveWithAnyArgs().IncrementAsync(default!, default);
    }
}
