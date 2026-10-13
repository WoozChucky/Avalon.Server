using System.Net;
using System.Net.Sockets;
using Avalon.Network.Packets;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Avalon.Server.Auth.UnitTests.Networking;

/// <summary>
/// End to end over loopback, through the real read loop both servers' connections run: what it does with a frame it
/// will not read, and with a frame that arrives while the connection is closing.
/// </summary>
public class ConnectionReadLoopShould
{
    private const int MaxInboundFrameSize = 1024;
    private static readonly TimeSpan s_wait = TimeSpan.FromSeconds(10);

    // The smallest frame the loop parses: two bytes, an empty header.
    private static readonly byte[] s_frame = [0x02, 0x0A, 0x00];

    private sealed class FakeServer : IServerBase
    {
        public int MaxInboundFrameSize => ConnectionReadLoopShould.MaxInboundFrameSize;
        public Task RemoveConnection(IConnection connection) => Task.CompletedTask;
        public Task CallListener(IConnection connection, NetworkPacketHeader header, Packet? payload) => Task.CompletedTask;
        public long ServerTime => 0;
        public int SendBufferCapacity => 16;
        public void CallConnectionListener(IConnection connection) { }
    }

    /// <summary>An outbox whose close waits for the test, so the connection stays closing with its socket up.</summary>
    private sealed class HeldOutbox : IOutbox
    {
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Seals => false;
        public void Connect(PacketStream stream) { }

        public bool Enqueue(OutboundPacket packet)
        {
            packet.Release();
            return false;
        }

        public ValueTask DisposeAsync() => new(Release.Task);
    }

    /// <summary>
    /// A connection whose <see cref="OnReceive" /> has no closing check of its own, as the auth server's has none: it
    /// stands for a handler being run.
    /// </summary>
    private sealed class ProbeConnection : Connection
    {
        public readonly TaskCompletionSource Closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource FirstHandled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource SecondSeen = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly HeldOutbox Outbox = new();
        public readonly IPacketReader Reader;
        private int _framesRefused;
        private int _handled;
        private int _bytesDiscarded;

        public ProbeConnection(TcpClient client, ILogger logger) : this(client, logger, NewReader())
        {
        }

        private ProbeConnection(TcpClient client, ILogger logger, IPacketReader reader)
            : base(logger, new FakeServer(), reader)
        {
            Reader = reader;
            Init(client);
        }

        private static IPacketReader NewReader()
        {
            IPacketReader reader = Substitute.For<IPacketReader>();
            reader.BufferSize.Returns(256);
            return reader;
        }

        public int FramesRefused => Volatile.Read(ref _framesRefused);
        public int Handled => Volatile.Read(ref _handled);
        public int BytesDiscarded => Volatile.Read(ref _bytesDiscarded);
        public int PacketsReceived => Volatile.Read(ref PacketReceivedCount);

        protected override void OnHandshakeFinished() { }

        protected override Task<PacketStream> GetStream(TcpClient client) =>
            Task.FromResult(new PacketStream(new NetworkStream(client.Client)));

        protected override IOutbox OnCreateOutbox() => Outbox;

        protected override Task OnClose(bool expected = true)
        {
            Closed.TrySetResult();
            return Task.CompletedTask;
        }

        protected override void OnFrameRefused() => Interlocked.Increment(ref _framesRefused);

        protected override void OnFrameDiscarded(int size)
        {
            Interlocked.Add(ref _bytesDiscarded, size);
            SecondSeen.TrySetResult();
        }

        protected override ValueTask OnReceive(NetworkPacketHeader header, Packet? payload)
        {
            if (Interlocked.Increment(ref _handled) == 1)
                FirstHandled.TrySetResult();
            else
                SecondSeen.TrySetResult();
            return ValueTask.CompletedTask;
        }

        protected override long GetServerTime() => 0;
    }

    private static async Task<(ProbeConnection Connection, TcpClient Client)> ConnectAsync(ILogger logger)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var client = new TcpClient();
        Task<TcpClient> accept = listener.AcceptTcpClientAsync();
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        var connection = new ProbeConnection(await accept, logger);
        await connection.StartAsync(CancellationToken.None);
        return (connection, client);
    }

    /// <summary>
    /// A peer whose frame declares more than <c>Hosting:MaxInboundFrameSize</c>, or whose length prefix is no length,
    /// is closed. Any peer can do this before it is admitted or logged in, so the close is counted once and says one
    /// line at Debug: nothing at Warning or above, and no exception through the read loop's "failed to read" path.
    /// </summary>
    [Theory]
    [InlineData(new byte[] { 0x81, 0x08 })]                               // 1,025: one more than the maximum
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01 })]       // six bytes: no length
    public async Task Close_a_peer_whose_frame_is_over_the_maximum_quietly_and_count_it_once(byte[] lengthPrefix)
    {
        var logger = new CapturingLogger();
        (ProbeConnection connection, TcpClient client) = await ConnectAsync(logger);
        using (client) using (connection)
        {
            connection.Outbox.Release.SetResult();
            // Less than the frame declares: before the limit, the read loop waited here for the rest.
            await client.GetStream().WriteAsync((byte[])[.. lengthPrefix, .. new byte[64]]);

            await connection.Closed.Task.WaitAsync(s_wait);

            Assert.Equal(1, connection.FramesRefused);
            Assert.Equal(0, connection.Handled);
            Assert.Equal(1, logger.Count(LogLevel.Debug));
            Assert.DoesNotContain(logger.Entries, entry => entry.Level >= LogLevel.Warning);
        }
    }

    /// <summary>
    /// Once a connection is closing, its socket stays up until what was queued for the peer has gone out, and the
    /// peer may still be sending. A frame that arrives then is read, so the close stays clean, and discarded: it runs
    /// no handler (the auth server's ran, on a connection that could no longer answer), and it is not decoded (the
    /// world dropped it only after decoding it). Its bytes are counted as received; it is not a packet received.
    /// </summary>
    [Fact]
    public async Task Read_and_discard_a_frame_that_arrives_while_closing_without_decoding_or_handling_it()
    {
        (ProbeConnection connection, TcpClient client) = await ConnectAsync(new CapturingLogger());
        using (client) using (connection)
        {
            await client.GetStream().WriteAsync(s_frame);
            await connection.FirstHandled.Task.WaitAsync(s_wait);

            connection.Close();
            await client.GetStream().WriteAsync(s_frame);
            await connection.SecondSeen.Task.WaitAsync(s_wait);

            Assert.Equal(1, connection.Handled);
            connection.Reader.ReceivedWithAnyArgs(1).Read(default, null);
            Assert.Equal(1, connection.PacketsReceived);
            Assert.Equal(s_frame.Length - 1, connection.BytesDiscarded);

            connection.Outbox.Release.SetResult();
            await connection.Closed.Task.WaitAsync(s_wait);
        }
    }
}
