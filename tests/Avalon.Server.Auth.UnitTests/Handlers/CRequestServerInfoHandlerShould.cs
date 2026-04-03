using Avalon.Common.Cryptography;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Handshake;
using Avalon.Server.Auth;
using Avalon.Server.Auth.Handlers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.Auth.UnitTests.Handlers;

public class CRequestServerInfoHandlerShould
{
    private readonly IAuthConnection _connection = Substitute.For<IAuthConnection>();
    private readonly IAvalonCryptoSession _cryptoSession = Substitute.For<IAvalonCryptoSession>();
    private readonly ICryptoManager _serverCrypto = Substitute.For<ICryptoManager>();
    private readonly CRequestServerInfoHandler _handler;

    public CRequestServerInfoHandlerShould()
    {
        _cryptoSession.Encrypt(Arg.Any<byte[]>()).Returns(x => (byte[])x[0]);
        _connection.CryptoSession.Returns(_cryptoSession);
        _serverCrypto.GetPublicKey().Returns(new byte[32]);
        _connection.ServerCrypto.Returns(_serverCrypto);
        _connection.Id.Returns(Guid.NewGuid());
        _handler = new CRequestServerInfoHandler(NullLoggerFactory.Instance);
    }

    [Fact]
    public async Task CloseConnection_WhenClientVersionIsOutdated()
    {
        var ctx = new AuthPacketContext<CRequestServerInfoPacket>
        {
            Packet = new CRequestServerInfoPacket { ClientVersion = "0.0.0" },
            Connection = _connection
        };

        await _handler.ExecuteAsync(ctx);

        _connection.Received(1).Close();
        _connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public async Task CloseConnection_WhenClientVersionIsEmpty()
    {
        var ctx = new AuthPacketContext<CRequestServerInfoPacket>
        {
            Packet = new CRequestServerInfoPacket { ClientVersion = string.Empty },
            Connection = _connection
        };

        await _handler.ExecuteAsync(ctx);

        _connection.Received(1).Close();
        _connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public async Task SendServerInfo_WhenClientVersionIsValid()
    {
        var ctx = new AuthPacketContext<CRequestServerInfoPacket>
        {
            Packet = new CRequestServerInfoPacket { ClientVersion = "0.0.1" },
            Connection = _connection
        };

        await _handler.ExecuteAsync(ctx);

        _connection.DidNotReceive().Close();
        _connection.Received(1).Send(Arg.Any<NetworkPacket>());
        _serverCrypto.Received(1).GetPublicKey();
    }
}
