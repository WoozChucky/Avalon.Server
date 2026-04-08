using Avalon.Common.Cryptography;
using Avalon.Common.ValueObjects;
using Avalon.Infrastructure.Services;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Abstractions;
using Avalon.Server.Auth.Handlers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.Auth.UnitTests.Handlers;

public class CMFAResetHandlerShould
{
    private readonly IMFAService _mfaService = Substitute.For<IMFAService>();
    private readonly IAuthConnection _connection = Substitute.For<IAuthConnection>();
    private readonly IAvalonCryptoSession _cryptoSession = Substitute.For<IAvalonCryptoSession>();

    private CMFAResetHandler CreateHandler() =>
        new(NullLoggerFactory.Instance, _mfaService);

    public CMFAResetHandlerShould()
    {
        _cryptoSession.Encrypt(Arg.Any<byte[]>()).Returns(x => (byte[])x[0]);
        _connection.CryptoSession.Returns(_cryptoSession);
        _connection.AccountId.Returns(new AccountId(1L));
    }

    [Fact]
    public async Task SendSuccess_WhenRecoveryCodesMatch()
    {
        _mfaService.ResetMFAAsync(Arg.Any<AccountId>(), "r1", "r2", "r3", Arg.Any<CancellationToken>())
            .Returns(new MFAResetResult(true, MFAOperationResult.Success));

        var ctx = new AuthPacketContext<CMFAResetPacket>
        {
            Packet = new CMFAResetPacket { RecoveryCode1 = "r1", RecoveryCode2 = "r2", RecoveryCode3 = "r3" },
            Connection = _connection
        };

        await CreateHandler().ExecuteAsync(ctx);

        _connection.Received(1).Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public async Task SendInvalidCode_WhenRecoveryCodesWrong()
    {
        _mfaService.ResetMFAAsync(Arg.Any<AccountId>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MFAResetResult(false, MFAOperationResult.InvalidCode));

        var ctx = new AuthPacketContext<CMFAResetPacket>
        {
            Packet = new CMFAResetPacket { RecoveryCode1 = "bad1", RecoveryCode2 = "bad2", RecoveryCode3 = "bad3" },
            Connection = _connection
        };

        await CreateHandler().ExecuteAsync(ctx);

        _connection.Received(1).Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public async Task CloseConnection_WhenNotAuthenticated()
    {
        _connection.AccountId.Returns((AccountId?)null);

        var ctx = new AuthPacketContext<CMFAResetPacket>
        {
            Packet = new CMFAResetPacket { RecoveryCode1 = "r1", RecoveryCode2 = "r2", RecoveryCode3 = "r3" },
            Connection = _connection
        };

        await CreateHandler().ExecuteAsync(ctx);

        _connection.Received(1).Close();
        await _mfaService.DidNotReceive().ResetMFAAsync(Arg.Any<AccountId>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
