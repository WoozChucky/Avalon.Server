using System.Net;
using System.Net.Sockets;
using Avalon.Configuration;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
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

        public ProbeConnection(TcpClient client, IServerBase server)
            : base(NullLogger.Instance, server, Substitute.For<IPacketReader>())
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

    private static async Task<(ProbeConnection Connection, TcpClient Client, TcpListener Listener)> Connect(ProxyProtocolConfiguration config)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var client = new TcpClient();
        Task<TcpClient> accept = listener.AcceptTcpClientAsync();
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        var connection = new ProbeConnection(await accept, new FakeServer(ProxyProtocolPolicy.From(config)));
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
            listener.Stop();
        }
    }

    [Fact]
    public async Task Drop_a_trusted_peer_that_sends_no_proxy_header()
    {
        var (connection, client, listener) = await Connect(new() { Enabled = true, TrustedProxies = ["127.0.0.0/8"] });
        using (client) using (connection)
        {
            byte[] notProxy = new byte[16];
            "\u0016\u0003\u0001 a TLS hello"u8.ToArray().CopyTo(notProxy, 0);
            await client.GetStream().WriteAsync(notProxy);
            await connection.StartAsync(CancellationToken.None);

            await connection.Closed.Task.WaitAsync(Wait);

            Assert.False(connection.StreamRequested.Task.IsCompleted);
            listener.Stop();
        }
    }

    [Fact]
    public async Task Drop_a_trusted_peer_that_stays_silent_past_the_timeout()
    {
        var (connection, client, listener) = await Connect(new() { Enabled = true, TrustedProxies = ["127.0.0.0/8"], HeaderTimeoutSeconds = 1 });
        using (client) using (connection)
        {
            await connection.StartAsync(CancellationToken.None);

            await connection.Closed.Task.WaitAsync(Wait);

            Assert.False(connection.StreamRequested.Task.IsCompleted);
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
