using Avalon.Network.Packets.Generic;
using Avalon.World;
using Avalon.World.Handlers;
using Avalon.World.Public;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Handlers;

public class PongHandlerShould
{
    private readonly IWorldConnection _connection = Substitute.For<IWorldConnection>();
    private readonly PongHandler _handler = new();

    [Fact]
    public void CallOnPongReceived_WithPacketTimestamps()
    {
        var packet = new CPongPacket
        {
            LastServerTimestamp = 1000L,
            ClientReceivedTimestamp = 2000L,
            ClientSentTimestamp = 3000L
        };

        _handler.Execute(_connection, packet);

        _connection.Received(1).OnPongReceived(1000L, 2000L, 3000L);
    }

    [Fact]
    public void CallOnPongReceived_WithZeroTimestamps()
    {
        var packet = new CPongPacket
        {
            LastServerTimestamp = 0L,
            ClientReceivedTimestamp = 0L,
            ClientSentTimestamp = 0L
        };

        _handler.Execute(_connection, packet);

        _connection.Received(1).OnPongReceived(0L, 0L, 0L);
    }
}
