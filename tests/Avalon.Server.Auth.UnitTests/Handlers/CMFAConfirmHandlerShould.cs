using Avalon.Common.Cryptography;
using Avalon.Common.ValueObjects;
using Avalon.Infrastructure.Services;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Abstractions;
using Avalon.Server.Auth.Handlers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.Auth.UnitTests.Handlers;

public class CMFAConfirmHandlerShould
{
    private readonly IMFAService _mfaService = Substitute.For<IMFAService>();
    private readonly IAuthConnection _connection = Substitute.For<IAuthConnection>();
    private readonly IAvalonCryptoSession _cryptoSession = new FakeAvalonCryptoSession();

    private CMFAConfirmHandler CreateHandler() =>
        new(NullLoggerFactory.Instance, _mfaService);

    public CMFAConfirmHandlerShould()
    {
        _connection.CryptoSession.Returns(_cryptoSession);
        _connection.AccountId.Returns(new AccountId(1L));
    }

    [Fact]
    public async Task SendRecoveryCodes_WhenCodeIsValid()
    {
        var codes = new[] { "code1", "code2", "code3" };
        _mfaService.ConfirmMFAAsync(Arg.Any<AccountId>(), "123456", Arg.Any<CancellationToken>())
            .Returns(new MFAConfirmResult(true, codes, MFAOperationResult.Success));

        var ctx = new AuthPacketContext<CMFAConfirmPacket>
        {
            Packet = new CMFAConfirmPacket { Code = "123456" },
            Connection = _connection
        };

        await CreateHandler().ExecuteAsync(ctx);

        _connection.Received(1).Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public async Task SendInvalidCode_WhenCodeIsWrong()
    {
        _mfaService.ConfirmMFAAsync(Arg.Any<AccountId>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MFAConfirmResult(false, null, MFAOperationResult.InvalidCode));

        var ctx = new AuthPacketContext<CMFAConfirmPacket>
        {
            Packet = new CMFAConfirmPacket { Code = "wrong" },
            Connection = _connection
        };

        await CreateHandler().ExecuteAsync(ctx);

        _connection.Received(1).Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public async Task CloseConnection_WhenNotAuthenticated()
    {
        _connection.AccountId.Returns((AccountId?)null);

        var ctx = new AuthPacketContext<CMFAConfirmPacket>
        {
            Packet = new CMFAConfirmPacket { Code = "123456" },
            Connection = _connection
        };

        await CreateHandler().ExecuteAsync(ctx);

        _connection.Received(1).Close();
        await _mfaService.DidNotReceive().ConfirmMFAAsync(Arg.Any<AccountId>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
