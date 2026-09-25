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

    private CMFAVerifyHandler CreateHandler() =>
        new(NullLoggerFactory.Instance, _mfaService, _accountRepository, _cache);

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
        await _accountRepository.Received(1).UpdateAsync(account);
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
        await _accountRepository.Received(1).UpdateAsync(account);
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
