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
        sender.EnqueuePing(clientTicks: 7, roundTrip: 8, offset: 9); // a ping queued before the close is not written after the notice
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
    /// A packet the session cannot seal (one that never completed its exchange, or spent its counter): the burst it was
    /// in is discarded, nothing of it reaches the wire, and the connection closes, refusing what is sent after.
    /// </summary>
    [Fact]
    public async Task Discard_the_burst_and_close_when_a_packet_cannot_be_sealed()
    {
        NetworkSendScheduler scheduler = Scheduler();
        var wire = new MemoryStream();
        int closes = 0;
        ConnectionSender sender = scheduler.CreateSender(Guid.NewGuid(), NullLogger.Instance, new RefusingSealer(), () => closes++);
        sender.Connect(new PacketStream(wire));
        var sealedHeader = new NetworkPacketHeader
        {
            Type = NetworkPacketType.SMSG_WORLD_STATE_UPDATE,
            Flags = NetworkPacketFlags.Encrypted,
            Protocol = NetworkProtocol.Tcp,
        };

        sender.Enqueue(Numbered(1));
        sender.Enqueue(new OutboundPacket(sealedHeader, _pool.Rent(new byte[10])));
        sender.Enqueue(Numbered(2));
        scheduler.RunPass(sender.OwnerThread);

        Assert.Equal(0, wire.Length);
        Assert.Equal(1, closes);
        Assert.Equal(0, sender.PendingBytes);
        Assert.False(sender.Enqueue(Numbered(3)));
        await CloseAsync(scheduler, sender);
        Assert.Equal(0, _pool.Outstanding);
    }

    /// <summary>
    /// A write that fails (the socket closed under it) closes the connection, refuses what is sent after, and lets the
    /// close finish at once, never waiting out its budget for a write that has already ended, with every segment back.
    /// </summary>
    [Fact]
    public async Task Close_the_connection_when_a_write_fails_and_finish_the_close_at_once()
    {
        NetworkSendScheduler scheduler = Scheduler();
        var failing = new PendingStream();
        int closes = 0;
        ConnectionSender sender = Open(scheduler, failing, () => Interlocked.Increment(ref closes));

        sender.Enqueue(Numbered(1));
        scheduler.RunPass(sender.OwnerThread);   // the write goes pending
        sender.Enqueue(Numbered(2));             // queued behind it
        failing.Fail(new IOException("the socket closed under the write"));
        await WaitUntil(() => Volatile.Read(ref closes) == 1, s_guard);

        Assert.False(sender.Enqueue(Numbered(3)));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await CloseAsync(scheduler, sender);
        Assert.True(watch.Elapsed < ConnectionSender.CloseBudget, $"the close took {watch.Elapsed}");
        Assert.Equal(1, failing.Writes);
        Assert.Equal(1, Volatile.Read(ref closes));
        Assert.Equal(0, sender.PendingBytes);
        Assert.Equal(0, _pool.Outstanding);
    }

    /// <summary>
    /// Review Focus 4: sends from other threads racing the tick's signal on running send threads. No packet may be
    /// left behind a dirty flag cleared at the wrong moment: the writers stop together at the end of each round, and
    /// everything they queued must go out with no further send (the tick's signals and the threads' timed wakes only
    /// drain the dirty lists, which a stranded connection is not on). Every third write ends asynchronously, as a socket
    /// with a full buffer does, so the end of a pending write races the sends too.
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
            SometimesPendingStream[] wires = [new(3), new(3)];
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

    [Fact]
    public async Task Close_a_connection_past_the_byte_cap_with_a_slow_connection_notice_as_its_only_packet()
    {
        NetworkSendScheduler scheduler = Scheduler(maxPendingBytes: 65_536);
        var wire = new MemoryStream();
        int closes = 0;
        ConnectionSender sender = Open(scheduler, wire, () => closes++);

        int accepted = 0;
        while (sender.Enqueue(Payload(4_096)))
            accepted++;
        Assert.Equal(16, accepted);                // 16 x 4 KiB is the cap; the 17th crosses it and is never queued
        Assert.False(sender.Enqueue(Payload(10))); // doomed: released at once, never dropped from a queue
        scheduler.RunPass(sender.OwnerThread);
        Assert.Equal(0, sender.PendingBytes);

        (NetworkPacketHeader header, byte[] payload) = Assert.Single(await Frames(wire.ToArray()));
        Assert.Equal(NetworkPacketType.SMSG_DISCONNECT, header.Type);
        Assert.Equal(DisconnectReason.SlowConnection, Serializer.Deserialize<SDisconnectPacket>(payload.AsSpan()).ReasonCode);
        Assert.Equal(1, closes);

        await CloseAsync(scheduler, sender);
        Assert.Equal(0, _pool.Outstanding);
    }

    /// <summary>
    /// Review Focus 3: a ping queued on a connection then found too slow is discarded with the rest of its queue.
    /// The disconnect notice stays its only packet, and nothing is stamped.
    /// </summary>
    [Fact]
    public async Task Discard_a_queued_ping_when_the_connection_is_closed_as_too_slow()
    {
        NetworkSendScheduler scheduler = Scheduler(maxPendingBytes: 65_536);
        var wire = new MemoryStream();
        ConnectionSender sender = Open(scheduler, wire);

        sender.EnqueuePing(clientTicks: 1, roundTrip: 2, offset: 3);
        while (sender.Enqueue(Payload(4_096))) { }
        scheduler.RunPass(sender.OwnerThread);

        (NetworkPacketHeader header, byte[] payload) = Assert.Single(await Frames(wire.ToArray()));
        Assert.Equal(NetworkPacketType.SMSG_DISCONNECT, header.Type);
        Assert.Equal(DisconnectReason.SlowConnection, Serializer.Deserialize<SDisconnectPacket>(payload.AsSpan()).ReasonCode);
        Assert.Equal(0, sender.LastPingServerTicks);
    }

    /// <summary>
    /// A notice and its close are two calls, and the owner can write the notice before the close begins: a ping queued
    /// with it, or after it, is never written behind it.
    /// </summary>
    [Fact]
    public async Task Write_no_ping_after_a_disconnect_notice_that_went_out_before_the_close()
    {
        NetworkSendScheduler scheduler = Scheduler();
        var wire = new MemoryStream();
        ConnectionSender sender = Open(scheduler, wire);

        sender.EnqueuePing(clientTicks: 1, roundTrip: 2, offset: 3);
        sender.Enqueue(SDisconnectPacket.Create("Server is shutting down", DisconnectReason.ServerShutdown, _encoder));
        scheduler.RunPass(sender.OwnerThread);
        sender.EnqueuePing(clientTicks: 4, roundTrip: 5, offset: 6);
        scheduler.RunPass(sender.OwnerThread);

        (NetworkPacketHeader header, byte[] _) = Assert.Single(await Frames(wire.ToArray()));
        Assert.Equal(NetworkPacketType.SMSG_DISCONNECT, header.Type);
        Assert.Equal(0, sender.LastPingServerTicks);
    }

    [Fact]
    public async Task Close_a_connection_whose_write_stalls_without_a_notice()
    {
        NetworkSendScheduler scheduler = Scheduler();
        var stalled = new PendingStream();
        int closes = 0;
        ConnectionSender sender = Open(scheduler, stalled, () => closes++);

        sender.Enqueue(Payload(100));
        scheduler.RunPass(sender.OwnerThread);   // the write goes pending
        _time.Advance(TimeSpan.FromSeconds(9));
        scheduler.RunPass(sender.OwnerThread);
        Assert.Equal(0, closes);                 // under the 10 s stall limit
        _time.Advance(TimeSpan.FromSeconds(2));
        scheduler.RunPass(sender.OwnerThread);   // over it: doomed and serviced in the same pass

        Assert.Equal(1, closes);
        Assert.True(sender.IsDoomed);
        Assert.Equal(1, stalled.Writes);         // no notice behind a write the peer is not reading

        stalled.Fail(new IOException("the socket closed under the write"));
        await CloseAsync(scheduler, sender);
        Assert.Equal(0, _pool.Outstanding);
    }

    /// <summary>
    /// Review Focus 5: shutdown meets a connection already doomed whose write never ends. Its shutdown notice is refused,
    /// its close is bounded by the close budget, the threads stop, and every queued segment is back in the pool.
    /// </summary>
    [Fact]
    public async Task Finish_closing_a_slow_connection_whose_write_never_ends_and_stop_the_threads()
    {
        NetworkSendScheduler scheduler = Scheduler(maxPendingBytes: 65_536);
        scheduler.Start();
        var stalled = new PendingStream();
        ConnectionSender sender = Open(scheduler, stalled);

        sender.Enqueue(Payload(100));
        await WaitUntil(() => stalled.Writes == 1, s_guard);   // pending: the peer stopped reading
        while (sender.Enqueue(Payload(4_096))) { }               // the queue grows past the cap: doomed

        Assert.False(sender.Enqueue(SDisconnectPacket.Create("Server is shutting down", DisconnectReason.ServerShutdown, _encoder)));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await sender.DisposeAsync().AsTask().WaitAsync(s_guard);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"the close took {watch.Elapsed}");
        Assert.Equal(100, sender.PendingBytes);   // the burst stays counted until its write ends; the queue was discarded

        Assert.True(scheduler.Stop(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, stalled.Writes);
        await WaitUntil(() => _pool.Outstanding == 0, s_guard);
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

    /// <summary>A stream whose every <c>k</c>-th write ends asynchronously, on the thread pool; the others end inline.</summary>
    private sealed class SometimesPendingStream(int k) : Stream
    {
        private readonly MemoryStream _written = new();
        private int _writes;

        public byte[] ToArray() => _written.ToArray();

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            // One write in flight at a time (the sender's rule), so the count and the buffer need no lock.
            if (++_writes % k != 0)
            {
                _written.Write(buffer.Span);
                return ValueTask.CompletedTask;
            }

            return WriteLaterAsync(buffer);
        }

        private async ValueTask WriteLaterAsync(ReadOnlyMemory<byte> buffer)
        {
            await Task.Yield();
            _written.Write(buffer.Span);
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
