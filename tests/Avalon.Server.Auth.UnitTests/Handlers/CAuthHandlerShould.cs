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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Avalon.Server.Auth.UnitTests.Handlers;

public class CAuthHandlerShould
{
    private readonly IAccountRepository _accountRepository = Substitute.For<IAccountRepository>();
    private readonly IReplicatedCache _cache = Substitute.For<IReplicatedCache>();
    private readonly IMFAHashService _mfaHashService = Substitute.For<IMFAHashService>();
    private readonly IAuthConnection _connection = Substitute.For<IAuthConnection>();
    private readonly IAvalonCryptoSession _cryptoSession = Substitute.For<IAvalonCryptoSession>();
    private readonly CAuthHandler _handler;

    private static IOptions<AuthConfiguration> AuthOptions(int maxFailedLogins = 5) =>
        Options.Create(new AuthConfiguration { MaxFailedLoginAttempts = maxFailedLogins });

    private CAuthHandler CreateHandler(int maxFailedLogins = 5) =>
        new(NullLoggerFactory.Instance, _accountRepository, _cache, _mfaHashService, AuthOptions(maxFailedLogins));

    public CAuthHandlerShould()
    {
        _cryptoSession.Encrypt(Arg.Any<byte[]>()).Returns(x => (byte[])x[0]);
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
}
