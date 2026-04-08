using Avalon.Common.Cryptography;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure.Services;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Abstractions;
using Avalon.Server.Auth.Configuration;
using Avalon.Server.Auth.Handlers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Avalon.Server.Auth.UnitTests.Handlers;

public class CMFASetupHandlerShould
{
    private readonly IMFAService _mfaService = Substitute.For<IMFAService>();
    private readonly IAccountRepository _accountRepository = Substitute.For<IAccountRepository>();
    private readonly IAuthConnection _connection = Substitute.For<IAuthConnection>();
    private readonly IAvalonCryptoSession _cryptoSession = Substitute.For<IAvalonCryptoSession>();

    private static IOptions<AuthConfiguration> AuthOptions() =>
        Options.Create(new AuthConfiguration { Issuer = "Avalon" });

    private CMFASetupHandler CreateHandler() =>
        new(NullLoggerFactory.Instance, _mfaService, _accountRepository, AuthOptions());

    private static Account MakeAccount(long id = 1) => new()
    {
        Id = new AccountId(id),
        Email = "test@test.com",
        Username = "TESTUSER",
        Salt = new byte[16],
        Verifier = new byte[16],
        JoinDate = DateTime.UtcNow
    };

    public CMFASetupHandlerShould()
    {
        _cryptoSession.Encrypt(Arg.Any<byte[]>()).Returns(x => (byte[])x[0]);
        _connection.CryptoSession.Returns(_cryptoSession);
        _connection.AccountId.Returns(new AccountId(1L));
    }

    [Fact]
    public async Task SendOtpUri_WhenSetupInitiated()
    {
        var account = MakeAccount();
        _accountRepository.FindByIdAsync(Arg.Any<AccountId>()).Returns(account);
        _mfaService.SetupMFAAsync(account, "Avalon", Arg.Any<CancellationToken>()).Returns(
            new MFASetupResult(true, "otpauth://totp/Avalon:test@test.com?secret=ABC", MFAOperationResult.Success));

        var ctx = new AuthPacketContext<CMFASetupPacket>
        {
            Packet = new CMFASetupPacket(),
            Connection = _connection
        };

        await CreateHandler().ExecuteAsync(ctx);

        _connection.Received(1).Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public async Task SendAlreadyEnabled_WhenMfaAlreadyConfirmed()
    {
        var account = MakeAccount();
        _accountRepository.FindByIdAsync(Arg.Any<AccountId>()).Returns(account);
        _mfaService.SetupMFAAsync(account, "Avalon", Arg.Any<CancellationToken>()).Returns(
            new MFASetupResult(false, null, MFAOperationResult.AlreadyEnabled));

        var ctx = new AuthPacketContext<CMFASetupPacket>
        {
            Packet = new CMFASetupPacket(),
            Connection = _connection
        };

        await CreateHandler().ExecuteAsync(ctx);

        _connection.Received(1).Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public async Task CloseConnection_WhenNotAuthenticated()
    {
        _connection.AccountId.Returns((AccountId?)null);

        var ctx = new AuthPacketContext<CMFASetupPacket>
        {
            Packet = new CMFASetupPacket(),
            Connection = _connection
        };

        await CreateHandler().ExecuteAsync(ctx);

        _connection.Received(1).Close();
        await _mfaService.DidNotReceive().SetupMFAAsync(Arg.Any<Account>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
