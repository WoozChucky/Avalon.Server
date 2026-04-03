using Avalon.Network.Packets.Generic;
using Avalon.Server.World.Handlers;
using Avalon.World;
using Avalon.World.Public;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Handlers;

public class PongHandlerShould
{
    private readonly IWorldConnection _connection = Substitute.For<IWorldConnection>();
    private readonly PongHandler _handler;

    public PongHandlerShould()
    {
        _handler = new PongHandler(NullLogger<PongHandler>.Instance);
    }

    [Fact]
    public async Task CallOnPongReceived_WithPacketTimestamps()
    {
        var ctx = new WorldPacketContext<CPongPacket>
        {
            Packet = new CPongPacket
            {
                LastServerTimestamp = 1000L,
                ClientReceivedTimestamp = 2000L,
                ClientSentTimestamp = 3000L
            },
            Connection = _connection
        };

        await _handler.ExecuteAsync(ctx);

        _connection.Received(1).OnPongReceived(1000L, 2000L, 3000L);
    }

    [Fact]
    public async Task CallOnPongReceived_WithZeroTimestamps()
    {
        var ctx = new WorldPacketContext<CPongPacket>
        {
            Packet = new CPongPacket
            {
                LastServerTimestamp = 0L,
                ClientReceivedTimestamp = 0L,
                ClientSentTimestamp = 0L
            },
            Connection = _connection
        };

        await _handler.ExecuteAsync(ctx);

        _connection.Received(1).OnPongReceived(0L, 0L, 0L);
    }
}
