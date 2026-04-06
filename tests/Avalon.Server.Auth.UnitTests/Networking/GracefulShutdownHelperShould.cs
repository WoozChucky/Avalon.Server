using Avalon.Common.Cryptography;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Generic;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

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

    [Fact]
    public void SendPacketWithCorrectReason_DuplicateLogin()
    {
        GracefulShutdownHelper.NotifyAndClose(_connection, "Your account has been logged in from another location.", DisconnectReason.DuplicateLogin);

        _connection.Received(1).Send(Arg.Is<NetworkPacket>(p => p.Header.Type == NetworkPacketType.SMSG_DISCONNECT));
        _connection.Received(1).Close();
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
        var logger = Substitute.For<ILogger>();
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
