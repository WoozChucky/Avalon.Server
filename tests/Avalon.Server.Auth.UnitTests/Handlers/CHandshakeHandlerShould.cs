using Avalon.Common.Cryptography;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Handshake;
using Avalon.Server.Auth;
using Avalon.Server.Auth.Handlers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.Auth.UnitTests.Handlers;

public class CHandshakeHandlerShould
{
    private readonly IAuthConnection _connection = Substitute.For<IAuthConnection>();
    private readonly IAvalonCryptoSession _cryptoSession = new FakeAvalonCryptoSession();
    private readonly CHandshakeHandler _handler;

    public CHandshakeHandlerShould()
    {
        _connection.CryptoSession.Returns(_cryptoSession);
        _connection.Id.Returns(Guid.NewGuid());
        _handler = new CHandshakeHandler(NullLoggerFactory.Instance);
    }

    [Fact]
    public async Task CloseConnection_WhenHandshakeVerificationFails()
    {
        _connection.VerifyHandshakeData(Arg.Any<byte[]>()).Returns(false);

        var ctx = new AuthPacketContext<CHandshakePacket>
        {
            Packet = new CHandshakePacket { HandshakeData = new byte[] { 1, 2, 3 } },
            Connection = _connection
        };

        await _handler.ExecuteAsync(ctx);

        _connection.Received(1).Close();
        _connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public async Task SendHandshakeResult_WhenHandshakeVerificationSucceeds()
    {
        _connection.VerifyHandshakeData(Arg.Any<byte[]>()).Returns(true);

        var ctx = new AuthPacketContext<CHandshakePacket>
        {
            Packet = new CHandshakePacket { HandshakeData = new byte[] { 1, 2, 3 } },
            Connection = _connection
        };

        await _handler.ExecuteAsync(ctx);

        _connection.DidNotReceive().Close();
        _connection.Received(1).Send(Arg.Any<NetworkPacket>());
    }
}
