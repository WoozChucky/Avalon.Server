using Avalon.Network;
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
}
