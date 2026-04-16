using Avalon.Common.Cryptography;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Handshake;
using Avalon.Server.Auth;
using Avalon.Server.Auth.Handlers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.Auth.UnitTests.Handlers;

public class CClientInfoHandlerShould
{
    private readonly IAuthConnection _connection = Substitute.For<IAuthConnection>();
    private readonly FakeAvalonCryptoSession _cryptoSession = new();
    private readonly ICryptoManager _serverCrypto = Substitute.For<ICryptoManager>();
    private readonly CClientInfoHandler _handler;

    private const int ValidKeySize = 32;

    public CClientInfoHandlerShould()
    {
        _connection.CryptoSession.Returns(_cryptoSession);
        _serverCrypto.GetValidKeySize().Returns(ValidKeySize);
        _connection.ServerCrypto.Returns(_serverCrypto);
        _connection.GenerateHandshakeData().Returns(new byte[16]);
        _connection.Id.Returns(Guid.NewGuid());
        _handler = new CClientInfoHandler(NullLoggerFactory.Instance);
    }

    [Fact]
    public async Task DoNothing_WhenPublicKeyIsNull()
    {
        var ctx = new AuthPacketContext<CClientInfoPacket>
        {
            Packet = new CClientInfoPacket { PublicKey = null! },
            Connection = _connection
        };

        await _handler.ExecuteAsync(ctx);

        Assert.Equal(0, _cryptoSession.InitializeCallCount);
        _connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public async Task DoNothing_WhenPublicKeyIsEmpty()
    {
        var ctx = new AuthPacketContext<CClientInfoPacket>
        {
            Packet = new CClientInfoPacket { PublicKey = Array.Empty<byte>() },
            Connection = _connection
        };

        await _handler.ExecuteAsync(ctx);

        Assert.Equal(0, _cryptoSession.InitializeCallCount);
        _connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public async Task DoNothing_WhenPublicKeySizeIsInvalid()
    {
        var invalidKey = new byte[ValidKeySize + 8]; // wrong size

        var ctx = new AuthPacketContext<CClientInfoPacket>
        {
            Packet = new CClientInfoPacket { PublicKey = invalidKey },
            Connection = _connection
        };

        await _handler.ExecuteAsync(ctx);

        Assert.Equal(0, _cryptoSession.InitializeCallCount);
        _connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public async Task InitializeCryptoAndSendHandshake_WhenPublicKeyIsValid()
    {
        var validKey = new byte[ValidKeySize];
        new Random().NextBytes(validKey);

        var ctx = new AuthPacketContext<CClientInfoPacket>
        {
            Packet = new CClientInfoPacket { PublicKey = validKey },
            Connection = _connection
        };

        await _handler.ExecuteAsync(ctx);

        Assert.Equal(1, _cryptoSession.InitializeCallCount);
        Assert.Equal(validKey, _cryptoSession.LastInitializedKey);
        _connection.Received(1).GenerateHandshakeData();
        _connection.Received(1).Send(Arg.Any<NetworkPacket>());
    }
}
