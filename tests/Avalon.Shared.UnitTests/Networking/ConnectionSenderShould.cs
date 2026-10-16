using System.Collections.Concurrent;
using Avalon.Common;
using Avalon.Common.Cryptography;
using Avalon.Configuration;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.Generic;
using Avalon.Network.Packets.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Org.BouncyCastle.Crypto;
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
            long stamp = Serializer.Deserialize<SPingPacket>(payload.AsSpan()).Rtt;
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
        sender.EnqueuePing(roundTrip: 8, offset: 9, tickMicros: 0); // a ping queued before the close is not written after the notice
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
        Assert.Equal(0, _pool.Outstanding); // ... framed ahead, its payload back in the pool, not held by the queue ...
        stalled.Complete();
        // The write's end runs where the stream completes it (the thread pool, under the test's context), which marks
        // the connection for its owner's next pass.
        PassUntil(scheduler, 0, () => stalled.Writes == 2);
        Assert.Equal(2, stalled.Writes);  // ... and goes as soon as it ends
    }

    /// <summary>
    /// While a write is pending the owner frames what is queued into the other burst buffer, over as many visits as
    /// it takes, and what is queued when the write ends goes behind it. Every packet reaches the wire once, in the
    /// order it was sent, across the burst in flight, the one framed ahead of it and the one after. The bytes are
    /// read when each write ends, as a socket reads them, so a burst framed over the one in flight would show.
    /// </summary>
    [Fact]
    public async Task Write_what_is_framed_ahead_of_a_pending_write_in_the_order_it_was_sent()
    {
        NetworkSendScheduler scheduler = Scheduler();
        var stalled = new PendingStream();
        ConnectionSender sender = Open(scheduler, stalled);
        int owner = sender.OwnerThread;

        sender.Enqueue(Numbered(1));
        scheduler.RunPass(owner);     // 1 goes out, and its write stays pending
        sender.Enqueue(Numbered(2));
        sender.Enqueue(Numbered(3));
        scheduler.RunPass(owner);     // framed ahead
        sender.Enqueue(Numbered(4));
        scheduler.RunPass(owner);     // framed ahead on a second visit, behind them
        Assert.Equal(1, stalled.Writes);
        sender.Enqueue(Numbered(5));  // still queued when the write ends
        stalled.Complete();
        PassUntil(scheduler, owner, () => stalled.Writes == 2);
        sender.Enqueue(Numbered(6));
        scheduler.RunPass(owner);     // framed ahead in its turn, into the buffer the first write used
        stalled.Complete();
        PassUntil(scheduler, owner, () => stalled.Writes == 3);
        Inline(stalled.Complete); // ended here, so the close finds no write in flight: its budget never runs out (#1019)

        Assert.Equal([1L, 2, 3, 4, 5, 6], (await Frames(stalled.ReadAtCompletion.ToArray()))
            .Select(frame => Serializer.Deserialize<SPingPacket>(frame.Payload.AsSpan()).Rtt));
        await CloseAsync(scheduler, sender);
        Assert.Equal(0, _pool.Outstanding);
    }

    /// <summary>
    /// A stall makes a connection frame ahead, and the burst buffer that holds it grows with what is queued, up to what
    /// <c>Network:MaxPendingBytes</c> allows; the next grows the other. They were kept for the connection's life
    /// (#883). A grown buffer is given back once the connection has gone <see cref="ConnectionSender.BurstTrimDelay" />
    /// without writing a burst that fills a quarter of it, and not before: a peer decides when its connection stalls
    /// and catches up, so a buffer given back as soon as it looked idle is one the peer has the server rent again in
    /// a loop. So a second stall inside the delay finds both buffers still there, the delay's end keeps them because
    /// they were filled within it, though the last burst before it was a small one (what counts is the largest burst
    /// of the delay, not the last), and only a quiet delay gives them back. And never while a write still reads one,
    /// or while a burst is framed ahead: when the delay ends during a pending write, the visits that frame ahead and
    /// that start the next write leave both buffers alone, and the first visit that finds both idle gives them back.
    /// A buffer given back in flight is rented again by the burst framed ahead of it, which would be written over the
    /// bytes still going out, so every packet carries its number and all must arrive, once and in order.
    /// </summary>
    [Fact]
    public async Task Give_back_a_grown_burst_buffer_only_after_a_delay_in_which_no_burst_filled_it()
    {
        const int Batch = 60; // 60 packets of 4 KiB: a burst of some 246 KiB, in a buffer grown to 256 KiB
        NetworkSendScheduler scheduler = Scheduler();
        var stalled = new PendingStream();
        ConnectionSender sender = Open(scheduler, stalled);
        int owner = sender.OwnerThread;
        int asNew = sender.BurstCapacity;
        int sent = 0;
        Assert.True(asNew < ConnectionSender.BurstTrimThreshold);

        Stall();
        int grown = sender.BurstCapacity;
        Assert.True(grown > 2 * Batch * 4_096);

        Small(); // caught up: from here the two buffers are watched
        Assert.Equal(grown, sender.BurstCapacity);
        _time.Advance(TimeSpan.FromSeconds(10));
        Stall(); // inside the delay: nothing was given back, so nothing is rented again
        Assert.Equal(grown, sender.BurstCapacity);
        Small(); // a small burst after the large ones: the last of the delay, not its largest
        _time.Advance(TimeSpan.FromSeconds(21));
        Small(); // the delay is over, and both buffers were filled within it: they stay
        Assert.Equal(grown, sender.BurstCapacity);
        _time.Advance(TimeSpan.FromSeconds(29));
        Write(100); // small bursts since, but not yet for the delay; this one's write stays pending
        Assert.Equal(grown, sender.BurstCapacity);
        _time.Advance(TimeSpan.FromSeconds(2));
        sender.Enqueue(Marked(sent++, 100));
        scheduler.RunPass(owner); // the delay ends on a visit that frames ahead of a pending write: nothing is idle
        Assert.Equal(grown, sender.BurstCapacity);
        EndWrite(startsAnother: true); // nor on the visit that starts the burst framed ahead
        Assert.Equal(grown, sender.BurstCapacity);
        EndWrite(startsAnother: false);
        Small(); // the first visit that finds both idle, after a whole delay of small bursts: both go back
        Assert.Equal(asNew, sender.BurstCapacity);

        Assert.Equal(Enumerable.Range(0, sent), (await Frames(stalled.ReadAtCompletion.ToArray()))
            .Select(frame => BitConverter.ToInt32(frame.Payload, 0)));
        await CloseAsync(scheduler, sender);
        Assert.Equal(0, _pool.Outstanding);

        // The peer stops reading: one buffer grows with what is framed ahead, then the other while the first is
        // still being written. Then it reads everything.
        void Stall()
        {
            Write(100);
            FrameAhead();
            EndWrite(startsAnother: true);
            FrameAhead();
            EndWrite(startsAnother: true);
            EndWrite(startsAnother: false);
        }

        void Small()
        {
            Write(100);
            EndWrite(startsAnother: false);
        }

        // With no write in flight: one packet, whose write starts and stays pending.
        void Write(int length)
        {
            int writes = stalled.Writes;
            sender.Enqueue(Marked(sent++, length));
            PassUntil(scheduler, owner, () => stalled.Writes > writes);
        }

        // With a write pending: a batch framed into the other buffer.
        void FrameAhead()
        {
            for (int packet = 0; packet < Batch; packet++)
                sender.Enqueue(Marked(sent++, 4_096));
            scheduler.RunPass(owner);
        }

        // The pending write ends, and the owner's next visit starts the burst framed ahead. The end runs here, inside
        // Complete, and not on the thread pool as under the test's synchronization context: what this case asserts is
        // which visit gives a buffer back, so a write still ending on another thread while the next burst is sent
        // would make that visit the one after.
        void EndWrite(bool startsAnother)
        {
            int writes = stalled.Writes;
            long pendingBefore = sender.PendingBytes;
            SynchronizationContext? context = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            try
            {
                stalled.Complete();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(context);
            }

            Assert.True(sender.PendingBytes < pendingBefore, "The write's end did not run inside Complete");
            PassUntil(scheduler, owner, () => startsAnother ? stalled.Writes > writes : sender.PendingBytes == 0);
        }
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
    /// The same failure while a write is pending: what was framed ahead goes with the packet that could not be sealed
    /// and is never written, whether the pending write then ends well or fails; the bytes still held are the burst in
    /// flight; and the connection is closed once, though the write its close breaks fails after the seal did.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Write_nothing_framed_ahead_and_close_once_when_a_packet_cannot_be_sealed_while_a_write_is_pending(
        bool pendingWriteFails)
    {
        NetworkSendScheduler scheduler = Scheduler();
        var stalled = new PendingStream();
        int closes = 0;
        ConnectionSender sender = scheduler.CreateSender(Guid.NewGuid(), NullLogger.Instance, new RefusingSealer(),
            () => Interlocked.Increment(ref closes));
        sender.Connect(new PacketStream(stalled));
        int owner = sender.OwnerThread;
        var sealedHeader = new NetworkPacketHeader
        {
            Type = NetworkPacketType.SMSG_WORLD_STATE_UPDATE,
            Flags = NetworkPacketFlags.Encrypted,
            Protocol = NetworkProtocol.Tcp,
        };

        OutboundPacket first = Numbered(1);
        int inFlight = first.PayloadLength;
        sender.Enqueue(first);
        scheduler.RunPass(owner);     // its write stays pending
        sender.Enqueue(Numbered(2));
        scheduler.RunPass(owner);     // framed ahead
        sender.Enqueue(new OutboundPacket(sealedHeader, _pool.Rent(new byte[10])));
        sender.Enqueue(Numbered(3));
        scheduler.RunPass(owner);     // the seal fails

        Assert.Equal(1, Volatile.Read(ref closes));
        Assert.Equal(inFlight, sender.PendingBytes);
        Assert.False(sender.Enqueue(Numbered(4)));

        // Ended here, so the close finds no write in flight: its budget never runs out on this clock (#1019).
        if (pendingWriteFails)
            Inline(() => stalled.Fail(new IOException("the socket closed under the write")));
        else
            Inline(stalled.Complete);
        PassUntil(scheduler, owner, () => sender.PendingBytes == 0);
        await CloseAsync(scheduler, sender);

        Assert.Equal(1, Volatile.Read(ref closes));
        Assert.Equal(1, stalled.Writes);
        (NetworkPacketHeader _, byte[] payload) = Assert.Single(await Frames(stalled.Written.ToArray()));
        Assert.Equal(1, Serializer.Deserialize<SPingPacket>(payload.AsSpan()).Rtt);
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
        // The close budget runs on the connection's clock, which never moves here: a close that waited it out would
        // never end, and the close's guard would fail it, however long the thread pool takes to run it (#1019).
        await CloseAsync(scheduler, sender);
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
    /// with a full buffer does, so the end of a pending write races the sends too, and what is sent meanwhile is framed
    /// ahead: each writer's packets must still reach the wire in the order it sent them.
    /// </summary>
    [Fact]
    public async Task Deliver_every_send_from_off_the_tick_while_the_tick_signals_running_send_threads()
    {
        const int Rounds = 200;
        const int PerRound = 25;
        NetworkSendScheduler scheduler = Scheduler(threads: 2);
        SometimesPendingStream[] wires = [new(3), new(3)];
        scheduler.Start();
        try
        {
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
                        senders[writer % 2].Enqueue(Numbered(writer * 1_000_000L + r * PerRound + seq));
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

            foreach (SometimesPendingStream wire in wires)
            {
                var last = new Dictionary<long, long>();
                List<(NetworkPacketHeader Header, byte[] Payload)> frames = await Frames(wire.ToArray());
                foreach ((NetworkPacketHeader _, byte[] payload) in frames)
                {
                    long stamp = Serializer.Deserialize<SPingPacket>(payload.AsSpan()).Rtt;
                    long writer = stamp / 1_000_000;
                    Assert.Equal(last.GetValueOrDefault(writer) + 1, stamp % 1_000_000);
                    last[writer] = stamp % 1_000_000;
                }

                Assert.Equal(2 * Rounds * PerRound, frames.Count);
            }

            Assert.Equal(0, _pool.Outstanding);
        }
        finally
        {
            Assert.True(scheduler.Stop(TimeSpan.FromSeconds(5)));
            foreach (SometimesPendingStream wire in wires)
                wire.Dispose();
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

        sender.EnqueuePing(roundTrip: 2, offset: 3, tickMicros: 0);
        while (sender.Enqueue(Payload(4_096))) { }
        scheduler.RunPass(sender.OwnerThread);

        (NetworkPacketHeader header, byte[] payload) = Assert.Single(await Frames(wire.ToArray()));
        Assert.Equal(NetworkPacketType.SMSG_DISCONNECT, header.Type);
        Assert.Equal(DisconnectReason.SlowConnection, Serializer.Deserialize<SDisconnectPacket>(payload.AsSpan()).ReasonCode);
        Assert.False(sender.TryTakePingStamp(_time.GetUtcNow().UtcTicks)); // the clock never moved: this would be its stamp
        Assert.False(sender.TryTakePongCredit()); // and no pong is let in for a ping that was never written

        await CloseAsync(scheduler, sender);
        Assert.Equal(0, _pool.Outstanding);
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

        sender.EnqueuePing(roundTrip: 2, offset: 3, tickMicros: 0);
        sender.Enqueue(SDisconnectPacket.Create("Server is shutting down", DisconnectReason.ServerShutdown, _encoder));
        scheduler.RunPass(sender.OwnerThread);
        sender.EnqueuePing(roundTrip: 5, offset: 6, tickMicros: 0);
        scheduler.RunPass(sender.OwnerThread);

        (NetworkPacketHeader header, byte[] _) = Assert.Single(await Frames(wire.ToArray()));
        Assert.Equal(NetworkPacketType.SMSG_DISCONNECT, header.Type);
        Assert.False(sender.TryTakePingStamp(_time.GetUtcNow().UtcTicks)); // the clock never moved: this would be its stamp
        Assert.False(sender.TryTakePongCredit()); // and no pong is let in for a ping that was never written

        await CloseAsync(scheduler, sender);
        Assert.Equal(0, _pool.Outstanding);
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

        // Ended inside Fail, so the close's passes find no write in flight: the close budget runs on a clock that
        // never moves here, so a close left waiting for the write would never end (#1019).
        Inline(() => stalled.Fail(new IOException("the socket closed under the write")));
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
        // The budget and the grace run on the connection's clock, which moves only here (#1019): the close is still
        // open a tick before the budget runs out, and ends once the budget and then the grace have.
        Task closing = sender.DisposeAsync().AsTask();
        // Longer than the budget and the grace on the wall clock: a close timed on it would have ended. On the
        // connection's clock, which has not moved, it cannot, so this never fails however slow the run.
        await Task.WhenAny(closing,
            Task.Delay(ConnectionSender.CloseBudget + ConnectionSender.CancelGrace + TimeSpan.FromMilliseconds(400)));
        Assert.False(closing.IsCompleted);
        Inline(() => _time.Advance(ConnectionSender.CloseBudget - TimeSpan.FromTicks(1)));
        Assert.False(closing.IsCompleted);
        using (var hang = new CancellationTokenSource(s_guard))
        {
            while (!closing.IsCompleted)
            {
                Inline(() => _time.Advance(ConnectionSender.CancelGrace)); // the budget, the cancel, then the grace
                await Task.Delay(1, hang.Token);
            }
        }

        await closing;
        // The owner, on its send thread, discards the queue of a connection found too slow; the burst stays counted
        // until its write ends, which it never does.
        await WaitUntil(() => sender.PendingBytes == 100, s_guard);
        Assert.True(scheduler.Stop(TimeSpan.FromSeconds(5)));
        Assert.Equal(100, sender.PendingBytes);
        Assert.Equal(1, stalled.Writes);
        await WaitUntil(() => _pool.Outstanding == 0, s_guard);
    }

    /// <summary>
    /// A broadcast's payload, encoded once and shared by its recipients (#875). Each recipient copies it into its own
    /// burst, sealing it there when it seals, and gives its reference back as it frames it: one that has closed refuses
    /// it at once, one whose write is still pending frames it ahead. Every other recipient writes the payload's own
    /// bytes, the segment is never sealed in place, and it goes back to the pool only once the last recipient has framed
    /// it, so a packet encoded into it afterwards changes nothing already framed.
    /// </summary>
    [Fact]
    public async Task Write_a_shared_payload_to_every_other_recipient_when_one_has_closed()
    {
        NetworkSendScheduler scheduler = Scheduler(threads: 2);
        (AvalonCryptoSession serverSession, AvalonCryptoSession clientSession) = PairedSessions();
        var plainWire = new MemoryStream();
        var sealedWire = new MemoryStream();
        var stalled = new PendingStream();
        ConnectionSender plain = Open(scheduler, plainWire);
        ConnectionSender sealing = Open(scheduler, sealedWire, sealer: serverSession);
        ConnectionSender pending = Open(scheduler, stalled);
        ConnectionSender closed = Open(scheduler, new MemoryStream());
        await CloseAsync(scheduler, closed);
        pending.Enqueue(Numbered(1));
        scheduler.RunAllPasses(); // the pending recipient's first write starts and stays pending

        // As MapInstance broadcasts a hit: encoded once, a reference per recipient, its own released after the loop.
        OutboundPacket hit = SUnitDamagePacket.Create(new ObjectGuid(ObjectType.Creature, 9), 7, 90, 10, _encoder);
        foreach (ConnectionSender recipient in new[] { plain, sealing, pending, closed })
            recipient.Enqueue(hit.Share());
        hit.Release();
        scheduler.RunAllPasses();

        // Framed by all three, refused by the closed one: the segment is back though one write has not ended.
        Assert.Equal(0, _pool.Outstanding);

        // Encoded into the segment the hit gave back: nothing framed above may change.
        OutboundPacket overwrite = SUnitDamagePacket.Create(new ObjectGuid(ObjectType.Creature, 1), 1, 1, 1, _encoder);
        Assert.Same(hit.Payload, overwrite.Payload); // the pool hands the hit's segment out again
        stalled.Complete();
        PassUntil(scheduler, pending.OwnerThread, () => stalled.Writes == 2);

        (NetworkPacketHeader plainHeader, byte[] plainPayload) = Assert.Single(await Frames(plainWire.ToArray()));
        Assert.Equal(0, (int)(plainHeader.Flags & NetworkPacketFlags.Encrypted));
        AssertHit(plainPayload);

        (NetworkPacketHeader sealedHeader, byte[] sealedPayload) = Assert.Single(await Frames(sealedWire.ToArray()));
        Assert.NotEqual(0, (int)(sealedHeader.Flags & NetworkPacketFlags.Encrypted));
        byte[] opened = new byte[sealedPayload.Length];
        AssertHit(opened[..clientSession.Decrypt(sealedPayload, opened)]);

        List<(NetworkPacketHeader Header, byte[] Payload)> late = await Frames(stalled.Written.ToArray());
        Assert.Equal(2, late.Count); // the first packet, then the hit framed ahead while its write was pending
        AssertHit(late[1].Payload);

        overwrite.Release();
        Assert.Equal(0, _pool.Outstanding);
    }

    private static void AssertHit(byte[] payload)
    {
        SUnitDamagePacket written = Serializer.Deserialize<SUnitDamagePacket>(payload.AsSpan());
        Assert.Equal(7ul, written.Target);
        Assert.Equal(10u, written.Damage);
    }

    /// <summary>A server session and the client session keyed against it, as admission keys the two.</summary>
    private static (AvalonCryptoSession Server, AvalonCryptoSession Client) PairedSessions()
    {
        AsymmetricCipherKeyPair clientKeys = AsymmetricCipher.GenerateECDHKeyPair();
        var client = new AvalonCryptoSession(CryptoRole.Client, clientKeys);
        var server = new AvalonCryptoSession(CryptoRole.Server);
        server.Initialize(AsymmetricCipher.GetPublicKeyBytes(AsymmetricCipher.GetPublicKeyFromKeyPair(clientKeys)));
        client.Initialize(server.GetPublicKey());
        return (server, client);
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

    private static ConnectionSender Open(NetworkSendScheduler scheduler, Stream stream, Action? close = null,
        IAvalonCryptoSession? sealer = null)
    {
        ConnectionSender sender = scheduler.CreateSender(Guid.NewGuid(), NullLogger.Instance, sealer, close ?? (() => { }));
        sender.Connect(new PacketStream(stream));
        return sender;
    }

    /// <summary>A plain packet that carries a number: an SPing's client timestamp.</summary>
    private OutboundPacket Numbered(long stamp) => SPingPacket.Create(0, stamp, 0, 0, _encoder);

    private OutboundPacket Payload(int length) =>
        new(new NetworkPacketHeader { Type = NetworkPacketType.SMSG_WORLD_STATE_UPDATE, Protocol = NetworkProtocol.Tcp },
            _pool.Rent(new byte[length]));

    /// <summary>A payload of <paramref name="length" /> bytes that starts with <paramref name="number" />.</summary>
    private OutboundPacket Marked(int number, int length)
    {
        byte[] payload = new byte[length];
        BitConverter.TryWriteBytes(payload, number);
        return new OutboundPacket(
            new NetworkPacketHeader { Type = NetworkPacketType.SMSG_WORLD_STATE_UPDATE, Protocol = NetworkProtocol.Tcp },
            _pool.Rent(payload));
    }

    /// <summary>The frames on a wire, read as the server reads its own (#875).</summary>
    private static Task<List<(NetworkPacketHeader Header, byte[] Payload)>> Frames(byte[] wire) =>
        ReadFramesAsync(wire).WaitAsync(s_guard);

    private static async Task<List<(NetworkPacketHeader Header, byte[] Payload)>> ReadFramesAsync(byte[] wire)
    {
        var frames = new List<(NetworkPacketHeader, byte[])>();
        await foreach (ReadOnlyMemory<byte> raw in new PacketStream(new MemoryStream(wire)).EnumerateRawFramesAsync(256, int.MaxValue))
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

    /// <summary>
    /// Runs <paramref name="action" /> outside the test's synchronization context, so the continuations it completes
    /// (a write's end, a close's budget) run inside it, as they do on a socket's I/O thread or a timer's, and not later
    /// on the thread pool, where a loaded test run can leave them waiting for seconds (#960).
    /// </summary>
    private static void Inline(Action action)
    {
        SynchronizationContext? context = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            action();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(context);
        }
    }

    /// <summary>
    /// A stream whose every <c>k</c>-th write ends asynchronously, on a completion thread of its own as a socket's ends on
    /// an I/O thread; the others end inline. Not on the thread pool: the rest of the test run can hold its workers for
    /// seconds (#960), and this case waits for some hundreds of these ends within its guard (#1019).
    /// </summary>
    private sealed class SometimesPendingStream : Stream
    {
        private readonly int _k;
        private readonly MemoryStream _written = new();
        private readonly BlockingCollection<(ReadOnlyMemory<byte> Buffer, TaskCompletionSource Done)> _pending = new();
        private int _writes;

        public SometimesPendingStream(int k)
        {
            _k = k;
            new Thread(CompleteWrites) { IsBackground = true, Name = "Test.WriteCompletion" }.Start();
        }

        public byte[] ToArray() => _written.ToArray();

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            // One write in flight at a time (the sender's rule), so the count and the buffer need no lock.
            if (++_writes % _k != 0)
            {
                _written.Write(buffer.Span);
                return ValueTask.CompletedTask;
            }

            // Ended on the completion thread, which runs the sender's continuation there, as an I/O completion does.
            var done = new TaskCompletionSource();
            _pending.Add((buffer, done), CancellationToken.None);
            return new ValueTask(done.Task);
        }

        private void CompleteWrites()
        {
            foreach ((ReadOnlyMemory<byte> buffer, TaskCompletionSource done) in _pending.GetConsumingEnumerable())
            {
                _written.Write(buffer.Span);
                done.SetResult();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _pending.CompleteAdding();
            base.Dispose(disposing);
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

        private ReadOnlyMemory<byte> _pending;

        /// <summary>The bytes of each write as it was handed them, as a socket copies them.</summary>
        public MemoryStream Written { get; } = new();

        /// <summary>
        /// The bytes of each write as its buffer held them when the write was ended: a socket whose buffer is full
        /// reads the rest of a write later, so the sender must leave the burst in flight alone until then.
        /// </summary>
        public MemoryStream ReadAtCompletion { get; } = new();

        public void Complete()
        {
            ReadAtCompletion.Write(_pending.Span);
            _pending = default;
            _write.TrySetResult();
        }

        public void Fail(Exception error) => _write.TrySetException(error);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Written.Write(buffer.Span);
            _pending = buffer;
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
