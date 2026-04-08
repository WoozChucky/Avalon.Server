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

namespace Avalon.Server.Auth.UnitTests.Handlers;

public class CMFAVerifyHandlerShould
{
    private readonly IMFAService _mfaService = Substitute.For<IMFAService>();
    private readonly IAccountRepository _accountRepository = Substitute.For<IAccountRepository>();
    private readonly IReplicatedCache _cache = Substitute.For<IReplicatedCache>();
    private readonly IAuthConnection _connection = Substitute.For<IAuthConnection>();
    private readonly IAvalonCryptoSession _cryptoSession = Substitute.For<IAvalonCryptoSession>();

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
        _cryptoSession.Encrypt(Arg.Any<byte[]>()).Returns(x => (byte[])x[0]);
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
}
