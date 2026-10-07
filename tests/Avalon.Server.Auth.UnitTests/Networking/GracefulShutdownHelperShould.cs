using Avalon.Network.Packets.Generic;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Avalon.Server.Auth.UnitTests.Networking;

public class GracefulShutdownHelperShould
{
    private readonly IConnection _connection;

    public GracefulShutdownHelperShould()
    {
        _connection = Substitute.For<IConnection>();
    }

    [Fact]
    public void SendDisconnectPacket_ThenClose()
    {
        GracefulShutdownHelper.NotifyAndClose(_connection, "Server is shutting down", DisconnectReason.ServerShutdown);

        Received.InOrder(() =>
        {
            _connection.Send(Arg.Is<NetworkPacket>(p => p.Header.Type == NetworkPacketType.SMSG_DISCONNECT));
            _connection.Close();
        });
    }

    /// <summary>
    /// The shutdown paths await this: the notice is delivered by the close itself, so a host that
    /// returned before the close finished would exit with the packet still queued.
    /// </summary>
    [Fact]
    public async Task SendDisconnectPacket_ThenAwaitTheClose()
    {
        var closed = new TaskCompletionSource();
        _connection.CloseAsync().Returns(closed.Task);

        Task notify = GracefulShutdownHelper.NotifyAndCloseAsync(_connection, "Server is shutting down", DisconnectReason.ServerShutdown);

        _connection.Received(1).Send(Arg.Is<NetworkPacket>(p => p.Header.Type == NetworkPacketType.SMSG_DISCONNECT));
        Assert.False(notify.IsCompleted, "Expected the caller to still be waiting on the close");

        closed.SetResult();
        await notify;
    }

    [Fact]
    public void Close_EvenWhenSendThrows()
    {
        _connection.When(c => c.Send(Arg.Any<NetworkPacket>())).Throw<InvalidOperationException>();

        GracefulShutdownHelper.NotifyAndClose(_connection, "Server is shutting down", DisconnectReason.ServerShutdown);

        _connection.Received(1).Close();
    }

    [Fact]
    public void LogWarning_WhenSendThrows()
    {
        ILogger logger = Substitute.For<ILogger>();
        _connection.When(c => c.Send(Arg.Any<NetworkPacket>())).Throw<InvalidOperationException>();

        GracefulShutdownHelper.NotifyAndClose(_connection, "Server is shutting down", DisconnectReason.ServerShutdown, logger);

        logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<InvalidOperationException>(),
            Arg.Any<Func<object, Exception?, string>>());
    }
}
