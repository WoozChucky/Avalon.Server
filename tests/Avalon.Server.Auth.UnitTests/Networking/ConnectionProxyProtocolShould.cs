using System.Net;
using System.Net.Sockets;
using Avalon.Configuration;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Avalon.Server.Auth.UnitTests.Networking;

/// <summary>
/// End to end over loopback: the PROXY header is read before the stream (and TLS) is set up, only
/// from trusted peers, and a trusted peer that does not send one is dropped.
/// </summary>
public class ConnectionProxyProtocolShould
{
    private sealed class FakeServer(ProxyProtocolPolicy policy) : IServerBase
    {
        public ProxyProtocolPolicy ProxyProtocol => policy;
        public Task RemoveConnection(IConnection connection) => Task.CompletedTask;
        public Task CallListener(IConnection connection, NetworkPacketHeader header, Packet? payload) => Task.CompletedTask;
        public long ServerTime => 0;
        public int SendBufferCapacity => 16;
        public void CallConnectionListener(IConnection connection) { }
    }

    private sealed class ProbeConnection : Connection
    {
        public readonly TaskCompletionSource StreamRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ProbeConnection(TcpClient client, IServerBase server, ILogger logger)
            : base(logger, server, Substitute.For<IPacketReader>())
        {
            Init(client);
        }

        protected override void OnHandshakeFinished() { }

        protected override Task<PacketStream> GetStream(TcpClient client)
        {
            StreamRequested.TrySetResult();
            return Task.FromResult(new PacketStream(new NetworkStream(client.Client)));
        }

        protected override Task OnClose(bool expected = true)
        {
            Closed.TrySetResult();
            return Task.CompletedTask;
        }

        protected override ValueTask OnReceive(NetworkPacketHeader header, Packet? payload) => ValueTask.CompletedTask;

        protected override long GetServerTime() => 0;
    }

    private static Task<(ProbeConnection Connection, TcpClient Client, TcpListener Listener)> Connect(ProxyProtocolConfiguration config) =>
        Connect(config, new CapturingLogger());

    private static async Task<(ProbeConnection Connection, TcpClient Client, TcpListener Listener)> Connect(ProxyProtocolConfiguration config, ILogger logger)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var client = new TcpClient();
        Task<TcpClient> accept = listener.AcceptTcpClientAsync();
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        var connection = new ProbeConnection(await accept, new FakeServer(ProxyProtocolPolicy.From(config)), logger);
        return (connection, client, listener);
    }

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Take_the_client_address_from_a_trusted_proxy_header()
    {
        var (connection, client, listener) = await Connect(new() { Enabled = true, TrustedProxies = ["127.0.0.0/8"] });
        using (client) using (connection)
        {
            await client.GetStream().WriteAsync(ProxyProtocolV2Should.ProxyTcp4("203.0.113.7", 51000));
            await connection.StartAsync(CancellationToken.None);

            await connection.StreamRequested.Task.WaitAsync(Wait);

            Assert.Equal("203.0.113.7:51000", connection.RemoteEndPoint);
            // Worked out once per connection, not per packet, for the telemetry.
            Assert.Equal("203.0.113.7", connection.ClientAddress);
            listener.Stop();
        }
    }

    [Fact]
    public async Task Ignore_proxy_headers_from_untrusted_peers()
    {
        var (connection, client, listener) = await Connect(new() { Enabled = true, TrustedProxies = ["10.42.0.0/16"] });
        using (client) using (connection)
        {
            await connection.StartAsync(CancellationToken.None);

            await connection.StreamRequested.Task.WaitAsync(Wait);

            Assert.StartsWith("127.0.0.1:", connection.RemoteEndPoint);
            Assert.Equal("127.0.0.1", connection.ClientAddress);
            listener.Stop();
        }
    }

    [Fact]
    public async Task Drop_a_trusted_peer_that_sends_no_proxy_header()
    {
        var logger = new CapturingLogger();
        var (connection, client, listener) = await Connect(new() { Enabled = true, TrustedProxies = ["127.0.0.0/8"] }, logger);
        using (client) using (connection)
        {
            byte[] notProxy = new byte[16];
            "\u0016\u0003\u0001 a TLS hello"u8.ToArray().CopyTo(notProxy, 0);
            await client.GetStream().WriteAsync(notProxy);
            await connection.StartAsync(CancellationToken.None);

            await connection.Closed.Task.WaitAsync(Wait);

            Assert.False(connection.StreamRequested.Task.IsCompleted);
            Assert.Equal(1, logger.Count(LogLevel.Warning));
            listener.Stop();
        }
    }

    [Fact]
    public async Task Drop_a_trusted_peer_that_stays_silent_past_the_timeout()
    {
        var logger = new CapturingLogger();
        var (connection, client, listener) = await Connect(new() { Enabled = true, TrustedProxies = ["127.0.0.0/8"], HeaderTimeoutSeconds = 1 }, logger);
        using (client) using (connection)
        {
            await connection.StartAsync(CancellationToken.None);

            await connection.Closed.Task.WaitAsync(Wait);

            Assert.False(connection.StreamRequested.Task.IsCompleted);
            Assert.Equal(1, logger.Count(LogLevel.Warning));
            listener.Stop();
        }
    }

    [Fact]
    public async Task Drop_a_trusted_peer_that_closes_without_sending_and_log_it_at_debug_only()
    {
        var logger = new CapturingLogger();
        var (connection, client, listener) = await Connect(new() { Enabled = true, TrustedProxies = ["127.0.0.0/8"] }, logger);
        using (connection)
        {
            // A TCP health check or port scan: connect, send nothing, close (#528).
            client.Dispose();
            await connection.StartAsync(CancellationToken.None);

            await connection.Closed.Task.WaitAsync(Wait);

            Assert.False(connection.StreamRequested.Task.IsCompleted);
            Assert.Equal(0, logger.Count(LogLevel.Warning));
            Assert.Equal(1, logger.Entries.Count(e => e.Level == LogLevel.Debug && e.Message.Contains("trusted proxy", StringComparison.Ordinal)));
            listener.Stop();
        }
    }

    [Fact]
    public async Task Drop_a_trusted_peer_that_sends_a_partial_header_and_log_a_warning()
    {
        var logger = new CapturingLogger();
        var (connection, client, listener) = await Connect(new() { Enabled = true, TrustedProxies = ["127.0.0.0/8"] }, logger);
        using (connection)
        {
            await client.GetStream().WriteAsync(ProxyProtocolV2Should.ProxyTcp4("203.0.113.7", 51000).AsMemory(0, 5));
            client.Dispose();
            await connection.StartAsync(CancellationToken.None);

            await connection.Closed.Task.WaitAsync(Wait);

            Assert.False(connection.StreamRequested.Task.IsCompleted);
            Assert.Equal(1, logger.Count(LogLevel.Warning));
            listener.Stop();
        }
    }

    [Fact]
    public async Task Leave_the_socket_address_alone_when_disabled()
    {
        var (connection, client, listener) = await Connect(new() { Enabled = false, TrustedProxies = ["127.0.0.0/8"] });
        using (client) using (connection)
        {
            await connection.StartAsync(CancellationToken.None);

            await connection.StreamRequested.Task.WaitAsync(Wait);

            Assert.StartsWith("127.0.0.1:", connection.RemoteEndPoint);
            listener.Stop();
        }
    }
}
