using Avalon.Common.Cryptography;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Auth;
using Avalon.Server.World.Handlers;
using Avalon.World;
using Avalon.World.Public;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Handlers;

public class WorldHandshakeHandlerShould
{
    private readonly IWorld _world = Substitute.For<IWorld>();
    private readonly IWorldConnection _connection = Substitute.For<IWorldConnection>();
    private readonly IAvalonCryptoSession _cryptoSession = Substitute.For<IAvalonCryptoSession>();
    private readonly WorldHandshakeHandler _handler;

    public WorldHandshakeHandlerShould()
    {
        _cryptoSession.Encrypt(Arg.Any<byte[]>()).Returns(x => (byte[])x[0]);
        _connection.CryptoSession.Returns(_cryptoSession);
        _connection.AccountId.Returns(new AccountId(1L));
        _world.MinVersion.Returns("0.0.1");
        _world.CurrentVersion.Returns("1.0.0");
        _handler = new WorldHandshakeHandler(NullLogger<WorldHandshakeHandler>.Instance, _world);
    }

    [Fact]
    public async Task SendHandshakeResult_WithAccountId()
    {
        var ctx = new WorldPacketContext<CWorldHandshakePacket>
        {
            Packet = new CWorldHandshakePacket { Version = "0.0.1" },
            Connection = _connection
        };

        await _handler.ExecuteAsync(ctx);

        _connection.Received(1).Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public async Task EnableTimeSyncWorker_AfterSendingHandshake()
    {
        var ctx = new WorldPacketContext<CWorldHandshakePacket>
        {
            Packet = new CWorldHandshakePacket { Version = "0.0.1" },
            Connection = _connection
        };

        await _handler.ExecuteAsync(ctx);

        _connection.Received(1).EnableTimeSyncWorker();
    }

    [Fact]
    public async Task ThrowInvalidOperationException_WhenAccountIdIsNull()
    {
        _connection.AccountId.Returns((AccountId?)null);

        var ctx = new WorldPacketContext<CWorldHandshakePacket>
        {
            Packet = new CWorldHandshakePacket { Version = "0.0.1" },
            Connection = _connection
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.ExecuteAsync(ctx));
    }
}
