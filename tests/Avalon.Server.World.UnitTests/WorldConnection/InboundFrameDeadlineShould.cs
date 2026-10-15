using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Avalon.Configuration;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abstractions;
using Avalon.World;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.WorldConnection;

/// <summary>
/// A world connection whose peer starts a frame and does not finish it is closed once the frame has had
/// <c>Hosting:InboundFrameTimeoutSeconds</c> (#938), on the world's clock, and counted as a protocol violation, through
/// the connection's real read loop over loopback.
/// </summary>
public sealed class InboundFrameDeadlineShould
{
    /// <summary>A hang guard only: nothing waits on it when the connection behaves.</summary>
    private static readonly TimeSpan s_guard = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Not the default, so a connection that ignored the server's setting would not close in time; and longer than the
    /// guard, so a deadline on the system clock would not close it within the test either.
    /// </summary>
    private static readonly TimeSpan s_deadline = TimeSpan.FromSeconds(60);

    /// <summary>A real world connection whose stream is the socket itself, counting the reads its loop starts.</summary>
    private sealed class LoopbackConnection(IWorldServer server, TcpClient client, IPacketReader reader, TimeProvider time)
        : Avalon.World.WorldConnection(server, client, NullLoggerFactory.Instance, reader, time)
    {
        public readonly TaskCompletionSource Closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly ConcurrentQueue<string> Violations = new();
        public readonly SemaphoreSlim ReadStarted = new(0);

        protected override Task<PacketStream> GetStream(TcpClient client) =>
            Task.FromResult(new PacketStream(new ReadCountingStream(new NetworkStream(client.Client), ReadStarted)));

        protected override IOutbox OnCreateOutbox() => new NoOutbox();

        protected override void OnProtocolViolation(string reason)
        {
            Violations.Enqueue(reason);
            base.OnProtocolViolation(reason);
        }

        protected override Task OnClose(bool expected = true)
        {
            Closed.TrySetResult();
            return Task.CompletedTask;
        }

        public async Task ReadAsync() => Assert.True(await ReadStarted.WaitAsync(s_guard), "the read loop did not read");
    }

    private sealed class NoOutbox : IOutbox
    {
        public bool Seals => false;
        public void Connect(PacketStream stream) { }

        public bool Enqueue(OutboundPacket packet)
        {
            packet.Release();
            return false;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>Signals each read it is asked for, before it waits on the socket.</summary>
    private sealed class ReadCountingStream(Stream inner, SemaphoreSlim readStarted) : Stream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            readStarted.Release();
            return inner.ReadAsync(buffer, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Close_a_connection_whose_started_frame_does_not_complete_within_the_deadline()
    {
        IWorldServer server = Substitute.For<IWorldServer, IServerBase>();
        ((IServerBase)server).ProxyProtocol.Returns(ProxyProtocolPolicy.Disabled);
        ((IServerBase)server).InboundFrameTimeout.Returns(s_deadline);
        var reader = new PacketReader(NullLoggerFactory.Instance, Options.Create(new HostingConfiguration()), []);
        var time = new FakeTimeProvider();

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var client = new TcpClient();
        Task<TcpClient> accept = listener.AcceptTcpClientAsync();
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        using TcpClient accepted = await accept;
        using var connection = new LoopbackConnection(server, accepted, reader, time);
        await connection.StartAsync(CancellationToken.None);
        await connection.ReadAsync();

        // Three bytes of a frame that declares ten: read, and the loop waits for the rest with the clock running.
        NetworkStream peer = client.GetStream();
        await peer.WriteAsync(new byte[] { 10, 0x01, 0x02 });
        await connection.ReadAsync();

        // Just short of the deadline the connection still reads what the peer trickles in.
        time.Advance(s_deadline - TimeSpan.FromTicks(1));
        await peer.WriteAsync(new byte[] { 0x03 });
        await connection.ReadAsync();
        Assert.False(connection.IsClosing);

        time.Advance(TimeSpan.FromTicks(1));
        await connection.Closed.Task.WaitAsync(s_guard);

        Assert.Equal(["frame_timeout"], connection.Violations);
    }
}
