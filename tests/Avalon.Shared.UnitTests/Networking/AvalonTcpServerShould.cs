using Avalon.Network;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Generic;
using Avalon.Network.Tcp;
using Avalon.Network.Tcp.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Avalon.Shared.UnitTests.Networking;

public class AvalonTcpServerShould
{
    /// <summary>
    /// Test-only subclass that bypasses socket setup and exposes the connection dictionary.
    /// ListenPort = 0 lets the OS assign a free ephemeral port, avoiding port conflicts.
    /// </summary>
    private sealed class TestableAvalonTcpServer : AvalonTcpServer
    {
        public TestableAvalonTcpServer()
            : base(
                NullLoggerFactory.Instance,
                new AvalonTcpServerConfiguration { Enabled = true, ListenPort = 0 },
                new CancellationTokenSource())
        {
            Running = true;
        }

        public void AddTestConnection(IAvalonTcpConnection connection)
            => _connections.TryAdd(connection.Id, connection);
    }

    private static IAvalonTcpConnection MockConnection()
    {
        var conn = Substitute.For<IAvalonTcpConnection>();
        conn.Id.Returns(Guid.NewGuid());
        return conn;
    }

    [Fact]
    public async Task CloseAllConnections_WhenStopAsyncCalled()
    {
        var server = new TestableAvalonTcpServer();
        var conn1 = MockConnection();
        var conn2 = MockConnection();
        var conn3 = MockConnection();
        server.AddTestConnection(conn1);
        server.AddTestConnection(conn2);
        server.AddTestConnection(conn3);

        await server.StopAsync();

        conn1.Received(1).Close();
        conn2.Received(1).Close();
        conn3.Received(1).Close();
    }

    [Fact]
    public async Task CloseRemainingConnections_WhenOneConnectionThrowsOnClose()
    {
        var server = new TestableAvalonTcpServer();
        var conn1 = MockConnection();
        var conn2 = MockConnection();
        var conn3 = MockConnection();
        conn2.When(c => c.Close()).Throws(new Exception("Simulated close failure"));
        server.AddTestConnection(conn1);
        server.AddTestConnection(conn2);
        server.AddTestConnection(conn3);

        // Must not throw
        await server.StopAsync();

        conn1.Received(1).Close();
        conn2.Received(1).Close();
        conn3.Received(1).Close();
    }

    [Fact]
    public async Task ReturnFalseForIsRunning_AfterStopAsync()
    {
        var server = new TestableAvalonTcpServer();

        await server.StopAsync();

        Assert.False(server.IsRunning);
    }

    [Fact]
    public async Task SendDisconnectPacket_BeforeClosingEachConnection()
    {
        var server = new TestableAvalonTcpServer();
        var conn1 = MockConnection();
        var conn2 = MockConnection();
        server.AddTestConnection(conn1);
        server.AddTestConnection(conn2);

        await server.StopAsync();

        await conn1.Received(1).SendAsync(Arg.Is<NetworkPacket>(p =>
            p.Header.Type == NetworkPacketType.SMSG_DISCONNECT));
        await conn2.Received(1).SendAsync(Arg.Is<NetworkPacket>(p =>
            p.Header.Type == NetworkPacketType.SMSG_DISCONNECT));
    }

    [Fact]
    public async Task SendDisconnectPacket_WithServerShutdownReason()
    {
        var server = new TestableAvalonTcpServer();
        NetworkPacket? captured = null;
        var conn = MockConnection();
        conn.SendAsync(Arg.Do<NetworkPacket>(p => captured = p)).Returns(Task.CompletedTask);
        server.AddTestConnection(conn);

        await server.StopAsync();

        Assert.NotNull(captured);
        // Deserialize and verify the reason code
        var inner = ProtoBuf.Serializer.Deserialize<SDisconnectPacket>(
            new ReadOnlyMemory<byte>(captured!.Payload));
        Assert.Equal(DisconnectReason.ServerShutdown, inner.ReasonCode);
    }

    [Fact]
    public async Task CloseConnection_EvenWhenSendDisconnectPacketThrows()
    {
        var server = new TestableAvalonTcpServer();
        var conn = MockConnection();
        conn.SendAsync(Arg.Any<NetworkPacket>()).ThrowsAsync(new IOException("Send failed"));
        server.AddTestConnection(conn);

        await server.StopAsync();

        conn.Received(1).Close();
    }

    [Fact]
    public async Task SendDisconnectPacket_BeforeClose_OrderVerified()
    {
        var server = new TestableAvalonTcpServer();
        var order = new List<string>();
        var conn = MockConnection();
        conn.SendAsync(Arg.Any<NetworkPacket>()).Returns(Task.CompletedTask)
            .AndDoes(_ => order.Add("send"));
        conn.When(c => c.Close()).Do(_ => order.Add("close"));
        server.AddTestConnection(conn);

        await server.StopAsync();

        Assert.Equal(["send", "close"], order);
    }
}
