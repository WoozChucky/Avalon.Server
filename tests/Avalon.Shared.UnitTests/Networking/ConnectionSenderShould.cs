using Avalon.Configuration;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Generic;
using Avalon.Network.Packets.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ProtoBuf;
using Xunit;

namespace Avalon.Shared.UnitTests.Networking;

/// <summary>
/// A connection's outbound side on the send threads (#875): many writers, one owner thread, one write in flight, never
/// a dropped packet, and every payload segment back in its pool however the connection ends. Most cases drive the
/// owner thread's passes from the test, on a scheduler whose threads never start; the races run on real threads.
/// </summary>
public sealed class ConnectionSenderShould
{
    private static readonly TimeSpan s_guard = TimeSpan.FromSeconds(30);

    private readonly PayloadSegmentPool _pool = new(countOutstanding: true);
    private readonly PacketEncoder _encoder;
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero));

    public ConnectionSenderShould() => _encoder = new PacketEncoder(_pool);

    [Fact]
    public async Task Write_every_packet_of_many_writers_in_each_writers_order_and_return_every_segment()
    {
        NetworkSendScheduler scheduler = Scheduler();
        var wire = new MemoryStream();
        ConnectionSender sender = Open(scheduler, wire);
        const long PerWriter = 2_000;

        var writers = Task.WhenAll(Enumerable.Range(1, 4).Select(writer => Task.Run(() =>
        {
            for (long seq = 1; seq <= PerWriter; seq++)
                sender.Enqueue(Numbered(writer * 1_000_000 + seq));
        })));
        PassUntil(scheduler, sender.OwnerThread, () => writers.IsCompleted);
        await writers.WaitAsync(s_guard);
        scheduler.RunPass(sender.OwnerThread);

        var last = new Dictionary<long, long>();
        int count = 0;
        foreach ((NetworkPacketHeader _, byte[] payload) in await Frames(wire.ToArray()))
        {
            long stamp = Serializer.Deserialize<SPingPacket>(payload.AsSpan()).ClientTimestamp;
            long writer = stamp / 1_000_000;
            Assert.Equal(last.GetValueOrDefault(writer) + 1, stamp % 1_000_000);
            last[writer] = stamp % 1_000_000;
            count++;
        }

        Assert.Equal(4 * PerWriter, count);
        Assert.Equal(0, _pool.Outstanding);
    }

    [Fact]
    public async Task Write_what_is_queued_at_close_with_the_disconnect_notice_last()
    {
        NetworkSendScheduler scheduler = Scheduler();
        var wire = new MemoryStream();
        ConnectionSender sender = Open(scheduler, wire);

        for (long stamp = 1; stamp <= 3; stamp++)
            sender.Enqueue(Numbered(stamp));
        sender.Enqueue(SDisconnectPacket.Create("Server is shutting down", DisconnectReason.ServerShutdown, _encoder));
        await CloseAsync(scheduler, sender);

        Assert.False(sender.Enqueue(Numbered(4)));
        Assert.Equal(
            [NetworkPacketType.SMSG_PING, NetworkPacketType.SMSG_PING, NetworkPacketType.SMSG_PING, NetworkPacketType.SMSG_DISCONNECT],
            (await Frames(wire.ToArray())).Select(frame => frame.Header.Type));
        Assert.Equal(0, _pool.Outstanding);
    }

    /// <summary>Review Focus 2: a socket that stopped draining holds its own connection only.</summary>
    [Fact]
    public async Task Keep_writing_the_other_connections_of_a_thread_while_one_write_is_pending()
    {
        NetworkSendScheduler scheduler = Scheduler(threads: 1);
        var stalled = new PendingStream();
        var wire = new MemoryStream();
        ConnectionSender slow = Open(scheduler, stalled);
        ConnectionSender fast = Open(scheduler, wire);

        slow.Enqueue(Numbered(1));
        fast.Enqueue(Numbered(1));
        scheduler.RunPass(0);
        slow.Enqueue(Numbered(2));
        fast.Enqueue(Numbered(2));
        scheduler.RunPass(0);

        Assert.Equal(2, (await Frames(wire.ToArray())).Count);
        Assert.Equal(1, stalled.Writes);  // one write in flight: the second packet waits for it ...
        stalled.Complete();
        // The write's end runs where the stream completes it (the thread pool, under the test's context), which marks
        // the connection for its owner's next pass.
        PassUntil(scheduler, 0, () => stalled.Writes == 2);
        Assert.Equal(2, stalled.Writes);  // ... and goes as soon as it ends
    }

    /// <summary>
    /// Review Focus 4: sends from other threads racing the tick's signal on running send threads. No packet may be
    /// left behind a dirty flag cleared at the wrong moment: the writers stop together at the end of each round, and
    /// everything they queued must go out with no further send (the tick's signals and the threads' timed wakes only
    /// drain the dirty lists, which a stranded connection is not on).
    /// </summary>
    [Fact]
    public async Task Deliver_every_send_from_off_the_tick_while_the_tick_signals_running_send_threads()
    {
        const int Rounds = 200;
        const int PerRound = 25;
        NetworkSendScheduler scheduler = Scheduler(threads: 2);
        scheduler.Start();
        try
        {
            MemoryStream[] wires = [new(), new()];
            ConnectionSender[] senders = [Open(scheduler, wires[0]), Open(scheduler, wires[1])];
            using var stop = new CancellationTokenSource();
            using var round = new Barrier(4);
            Task tick = Dedicated(() =>
            {
                while (!stop.IsCancellationRequested)
                    scheduler.SignalAll();
            });

            await Task.WhenAll(Enumerable.Range(0, 4).Select(writer => Dedicated(() =>
            {
                for (int r = 0; r < Rounds; r++)
                {
                    Assert.True(round.SignalAndWait(s_guard));
                    for (long seq = 1; seq <= PerRound; seq++)
                        senders[writer % 2].Enqueue(Numbered(seq));
                    Assert.True(round.SignalAndWait(s_guard));
                    if (writer == 0)
                    {
                        Assert.True(SpinWait.SpinUntil(() => senders.All(sender => sender.PendingBytes == 0), s_guard),
                            $"Round {r} never drained: a send was left behind");
                    }
                }
            }))).WaitAsync(s_guard);
            await stop.CancelAsync();
            await tick.WaitAsync(s_guard);

            Assert.Equal(2 * Rounds * PerRound, (await Frames(wires[0].ToArray())).Count);
            Assert.Equal(2 * Rounds * PerRound, (await Frames(wires[1].ToArray())).Count);
            Assert.Equal(0, _pool.Outstanding);
        }
        finally
        {
            Assert.True(scheduler.Stop(TimeSpan.FromSeconds(5)));
        }
    }

    private NetworkSendScheduler Scheduler(int threads = 1, int maxPendingBytes = 512 * 1024)
    {
        var options = new NetworkConfiguration
        {
            SendThreads = threads,
            MaxPendingBytes = maxPendingBytes,
            MaxWriteStall = TimeSpan.FromSeconds(10),
        };
        return new NetworkSendScheduler(options, NullLoggerFactory.Instance, _time, NetworkSendMetrics.Disabled, _encoder);
    }

    private static ConnectionSender Open(NetworkSendScheduler scheduler, Stream stream, Action? close = null)
    {
        ConnectionSender sender = scheduler.CreateSender(Guid.NewGuid(), NullLogger.Instance, sealer: null, close ?? (() => { }));
        sender.Connect(new PacketStream(stream));
        return sender;
    }

    /// <summary>A plain packet that carries a number: an SPing's client timestamp.</summary>
    private OutboundPacket Numbered(long stamp) => SPingPacket.Create(0, stamp, 0, 0, _encoder);

    private OutboundPacket Payload(int length) =>
        new(new NetworkPacketHeader { Type = NetworkPacketType.SMSG_WORLD_STATE_UPDATE, Protocol = NetworkProtocol.Tcp },
            _pool.Rent(new byte[length]));

    /// <summary>The frames on a wire, read as the server reads its own (#875).</summary>
    private static Task<List<(NetworkPacketHeader Header, byte[] Payload)>> Frames(byte[] wire) =>
        ReadFramesAsync(wire).WaitAsync(s_guard);

    private static async Task<List<(NetworkPacketHeader Header, byte[] Payload)>> ReadFramesAsync(byte[] wire)
    {
        var frames = new List<(NetworkPacketHeader, byte[])>();
        await foreach (ReadOnlyMemory<byte> raw in new PacketStream(new MemoryStream(wire)).EnumerateRawFramesAsync(256))
        {
            var frame = InboundPacketFrame.ParseFrame(raw);
            frames.Add((frame.Header, frame.Payload.ToArray()));
        }

        return frames;
    }

    /// <summary>Closes as <c>Connection</c> does, running the owner's passes the close waits for.</summary>
    private static async Task CloseAsync(NetworkSendScheduler scheduler, ConnectionSender sender)
    {
        Task closing = sender.DisposeAsync().AsTask();
        for (int pass = 0; pass < 10 && !closing.IsCompleted; pass++)
        {
            scheduler.RunPass(sender.OwnerThread);
            await Task.Yield();
        }

        await closing.WaitAsync(s_guard);
    }

    /// <summary>Runs the owner's passes from the test until <paramref name="condition" /> holds, within the guard.</summary>
    private static void PassUntil(NetworkSendScheduler scheduler, int thread, Func<bool> condition)
    {
        long deadline = Environment.TickCount64 + (long)s_guard.TotalMilliseconds;
        while (!condition())
        {
            Assert.True(Environment.TickCount64 < deadline, "The owner's passes never got there");
            scheduler.RunPass(thread);
            Thread.Yield();
        }
    }

    /// <summary>A thread of its own, so writers held at a barrier never wait for the thread pool to grow.</summary>
    private static Task Dedicated(Action body) =>
        Task.Factory.StartNew(body, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private static async Task WaitUntil(Func<bool> condition, TimeSpan limit)
    {
        using var timeout = new CancellationTokenSource(limit);
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    /// <summary>A stream whose writes stay pending until the test ends them, as a socket whose peer stopped reading.</summary>
    private sealed class PendingStream : Stream
    {
        private TaskCompletionSource _write = new();

        public int Writes { get; private set; }

        public void Complete() => _write.TrySetResult();

        public void Fail(Exception error) => _write.TrySetException(error);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Writes++;
            _write = new TaskCompletionSource();
            return new ValueTask(_write.Task);
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
