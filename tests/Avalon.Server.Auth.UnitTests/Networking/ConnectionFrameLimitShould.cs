using System.Net;
using System.Net.Sockets;
using Avalon.Network.Packets;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Avalon.Server.Auth.UnitTests.Networking;

/// <summary>
/// End to end over loopback, through the real read loop: a peer whose frame declares more than
/// <c>Hosting:MaxInboundFrameSize</c>, or whose length prefix is no length, is closed. Any peer can do this before it
/// is admitted or logged in, so the close is counted once and says one line at Debug: nothing at Warning or above,
/// and no exception through the read loop's "failed to read" path.
/// </summary>
public class ConnectionFrameLimitShould
{
    private const int MaxInboundFrameSize = 1024;
    private static readonly TimeSpan s_wait = TimeSpan.FromSeconds(10);

    private sealed class FakeServer : IServerBase
    {
        public int MaxInboundFrameSize => ConnectionFrameLimitShould.MaxInboundFrameSize;
        public Task RemoveConnection(IConnection connection) => Task.CompletedTask;
        public Task CallListener(IConnection connection, NetworkPacketHeader header, Packet? payload) => Task.CompletedTask;
        public long ServerTime => 0;
        public int SendBufferCapacity => 16;
        public void CallConnectionListener(IConnection connection) { }
    }

    private sealed class ProbeConnection : Connection
    {
        public readonly TaskCompletionSource Closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _framesRefused;
        private int _received;

        public ProbeConnection(TcpClient client, ILogger logger)
            : base(logger, new FakeServer(), Reader())
        {
            Init(client);
        }

        private static IPacketReader Reader()
        {
            IPacketReader reader = Substitute.For<IPacketReader>();
            reader.BufferSize.Returns(256);
            return reader;
        }

        public int FramesRefused => Volatile.Read(ref _framesRefused);
        public int Received => Volatile.Read(ref _received);

        protected override void OnHandshakeFinished() { }

        protected override Task<PacketStream> GetStream(TcpClient client) =>
            Task.FromResult(new PacketStream(new NetworkStream(client.Client)));

        protected override Task OnClose(bool expected = true)
        {
            Closed.TrySetResult();
            return Task.CompletedTask;
        }

        protected override void OnFrameRefused() => Interlocked.Increment(ref _framesRefused);

        protected override ValueTask OnReceive(NetworkPacketHeader header, Packet? payload)
        {
            Interlocked.Increment(ref _received);
            return ValueTask.CompletedTask;
        }

        protected override long GetServerTime() => 0;
    }

    [Theory]
    [InlineData(new byte[] { 0x81, 0x08 })]                               // 1,025: one more than the maximum
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01 })]       // six bytes: no length
    public async Task Close_a_peer_whose_frame_is_over_the_maximum_quietly_and_count_it_once(byte[] lengthPrefix)
    {
        var logger = new CapturingLogger();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var client = new TcpClient();
        Task<TcpClient> accept = listener.AcceptTcpClientAsync();
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        using var connection = new ProbeConnection(await accept, logger);

        await connection.StartAsync(CancellationToken.None);
        // Less than the frame declares: before the limit, the read loop waited here for the rest.
        await client.GetStream().WriteAsync((byte[])[.. lengthPrefix, .. new byte[64]]);

        await connection.Closed.Task.WaitAsync(s_wait);

        Assert.Equal(1, connection.FramesRefused);
        Assert.Equal(0, connection.Received);
        Assert.Equal(1, logger.Count(LogLevel.Debug));
        Assert.DoesNotContain(logger.Entries, entry => entry.Level >= LogLevel.Warning);
    }
}
