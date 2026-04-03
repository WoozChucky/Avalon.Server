using System.Net;
using System.Net.Sockets;
using Avalon.Common.Cryptography;
using Avalon.Configuration;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Avalon.Server.Auth.UnitTests.Networking;

/// <summary>
/// Integration-level tests for ServerBase shutdown behaviour.
/// These use real TCP sockets on ephemeral ports — no external services needed.
/// </summary>
public class ServerBaseShould
{
    // ── Minimal test doubles ────────────────────────────────────────────────

    /// <summary>
    /// A no-op IConnection that satisfies the ServerBase generic constraint.
    /// ActivatorUtilities creates it with (TcpClient, IServerBase) based on the
    /// OnClientAccepted factory pattern in ServerBase.
    /// Because the test never connects a real client these fields are never used.
    /// </summary>
    private sealed class StubConnection : BackgroundService, IConnection
    {
        // ReSharper disable once UnusedParameter.Local — required by ActivatorUtilities
        public StubConnection(System.Net.Sockets.TcpClient _, IServerBase __)
        {
            Id = Guid.NewGuid();
        }

        public Guid Id { get; }
        public new Task? ExecuteTask => ExecuteAsync(CancellationToken.None);
        public string RemoteEndPoint => "stub";
        public IAvalonCryptoSession CryptoSession => null!;
        public ICryptoManager ServerCrypto => null!;
        public void Close(bool expected = true) { }
        public void Send(NetworkPacket packet) { }
        public new Task StartAsync(CancellationToken token = default) => Task.CompletedTask;
        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
    }

    private sealed class TestServerBase : ServerBase<StubConnection>
    {
        public TestServerBase(IPacketManager packetManager, IOptions<HostingConfiguration> opts)
            : base(packetManager, NullLogger.Instance, Substitute.For<IServiceProvider>(), opts) { }

        protected override object GetContextPacket(IConnection connection, object? packet, Type packetType)
            => null!;

        protected override Task OnStoppingAsync(CancellationToken stoppingToken)
            => Task.CompletedTask;

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
            => Task.Delay(Timeout.Infinite, stoppingToken);
    }

    // ── Helper ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Asks the OS for a free ephemeral port so tests never conflict.
    /// </summary>
    private static ushort GetFreePort()
    {
        var tmp = new TcpListener(IPAddress.Loopback, 0);
        tmp.Start();
        int port = ((IPEndPoint)tmp.LocalEndpoint).Port;
        tmp.Stop();
        return (ushort)port;
    }

    private static TestServerBase CreateServer(ushort port)
    {
        var opts = Options.Create(new HostingConfiguration { Host = "127.0.0.1", Port = port });
        return new TestServerBase(Substitute.For<IPacketManager>(), opts);
    }

    // ── Tests ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The key regression test: after StopAsync the OS port must be free.
    /// Before the fix, Listener.Stop() was never called, leaving the socket
    /// bound and any attempt to re-bind the same port would throw.
    /// </summary>
    [Fact]
    public async Task ReleaseListeningPort_AfterStopAsync()
    {
        ushort port = GetFreePort();
        var server = CreateServer(port);

        await server.StartAsync(CancellationToken.None);
        await server.StopAsync(CancellationToken.None);

        // If the listener socket was properly closed, we can bind the same port again.
        var probe = new TcpListener(IPAddress.Loopback, port);
        probe.Start(); // Throws SocketException if port is still held.
        probe.Stop();
    }

    /// <summary>
    /// StopAsync must complete without throwing even when there are no active connections.
    /// </summary>
    [Fact]
    public async Task CompleteWithoutException_WhenStoppedWithNoConnections()
    {
        ushort port = GetFreePort();
        var server = CreateServer(port);

        await server.StartAsync(CancellationToken.None);

        var exception = await Record.ExceptionAsync(() => server.StopAsync(CancellationToken.None));

        Assert.Null(exception);
    }
}
