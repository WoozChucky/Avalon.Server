using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Avalon.Common.Cryptography;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Generic;
using Avalon.Network.Packets.Serialization;
using Microsoft.Extensions.Logging;

namespace Avalon.Hosting.Networking;

/// <summary>
/// One world connection's outbound side (#875). Any thread enqueues: the tick, the read loop, a Redis callback, the
/// shutdown. The connection's owner send thread (<see cref="NetworkSendScheduler" />) drains the queue into one burst,
/// seals what this connection seals, frames it and writes it, with one write in flight at a time. Nothing is dropped.
/// </summary>
/// <remarks>
/// Owner-thread only: <see cref="Service" />, <see cref="Fault" />, the burst buffer, <c>_finishedFlag</c>,
/// <c>_doomHandled</c> and <c>_noticeWritten</c>. Every other field is read and written with <see cref="Interlocked" />
/// or <see cref="Volatile" />.
/// </remarks>
public sealed class ConnectionSender : IOutbox
{
    /// <summary>How long a close waits for what is queued to go out (the old outbox's budget).</summary>
    public static readonly TimeSpan CloseBudget = TimeSpan.FromMilliseconds(500);

    /// <summary>Past <see cref="CloseBudget" /> the write in flight is cancelled; how long it is given to unwind.</summary>
    public static readonly TimeSpan CancelGrace = TimeSpan.FromMilliseconds(100);

    /// <summary>What a connection closed past the byte cap is told.</summary>
    public const string SlowConnectionMessage = "Your connection could not keep up with the server.";

    // The time-sync ping's place in the queue: no payload, since the owner encodes the ping as it writes it.
    private static readonly OutboundPacket s_pingMarker = new(
        new NetworkPacketHeader { Type = NetworkPacketType.SMSG_PING, Protocol = NetworkProtocol.Tcp }, payload: null);

    private readonly ConcurrentQueue<OutboundPacket> _queue = new();
    private readonly NetworkSendScheduler _scheduler;
    private readonly ILogger _logger;
    private readonly IAvalonCryptoSession? _sealer;
    private readonly PacketEncoder _encoder;
    private readonly Action _close;
    private readonly TimeProvider _time;
    private readonly PooledArrayBufferWriter _burst = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Action _onPendingWriteCompleted;
    private readonly long _maxPendingBytes;

    private PacketStream? _stream;
    private ConfiguredValueTaskAwaitable.ConfiguredValueTaskAwaiter _pendingWrite;
    private long _pendingBytes;
    private long _writeStarted;
    private int _burstPayloadBytes;
    private int _dirty;
    private int _writeInFlight;
    private int _completed;
    private int _activeEnqueues;
    private int _faulted;
    private int _closeRequested;
    private int _doomed;
    private long _pingClientTicks;
    private long _pingRoundTrip;
    private long _pingOffset;
    private long _lastPingServerTicks;
    private bool _finishedFlag;
    private bool _doomHandled; // owner thread only
    private bool _noticeWritten; // owner thread only: a disconnect notice went out, so no ping follows it

    internal ConnectionSender(NetworkSendScheduler scheduler, int ownerThread, Guid connectionId, ILogger logger,
        IAvalonCryptoSession? sealer, PacketEncoder encoder, Action close, TimeProvider time, long maxPendingBytes)
    {
        _scheduler = scheduler;
        OwnerThread = ownerThread;
        ConnectionId = connectionId;
        _logger = logger;
        _sealer = sealer;
        _encoder = encoder;
        _close = close;
        _time = time;
        _onPendingWriteCompleted = OnPendingWriteCompleted;
        _maxPendingBytes = maxPendingBytes;
    }

    public Guid ConnectionId { get; }

    /// <summary>The send thread that owns this connection for its life.</summary>
    public int OwnerThread { get; }

    /// <summary>Whether this connection seals what is flagged Encrypted (the session layer inside TLS).</summary>
    public bool Seals => _sealer is not null;

    /// <summary>Payload bytes queued plus those of the burst being written.</summary>
    public long PendingBytes => Interlocked.Read(ref _pendingBytes);

    /// <summary>True once the connection was found too slow; it is closing.</summary>
    public bool IsDoomed => Volatile.Read(ref _doomed) != 0;

    /// <summary>The server timestamp of the last ping written, in UTC ticks; 0 before the first.</summary>
    public long LastPingServerTicks => Interlocked.Read(ref _lastPingServerTicks);

    internal bool IsWriteInFlight => Volatile.Read(ref _writeInFlight) != 0;

    internal long WriteStartedTimestamp => Volatile.Read(ref _writeStarted);

    /// <summary>Owner thread only: whether the owner's list of writing connections holds this one.</summary>
    internal bool Tracked { get; set; }

    public void Connect(PacketStream stream)
    {
        _stream = stream;
        MarkDirty();
    }

    /// <summary>
    /// Takes the packet's payload reference. O(1); never blocks, throws or allocates. A closed connection refuses the
    /// packet and releases it at once, as does one found too slow: a packet that would take it past
    /// <c>Network:MaxPendingBytes</c> is refused, and the connection closed with <c>DisconnectReason.SlowConnection</c>.
    /// </summary>
    public bool Enqueue(OutboundPacket packet)
    {
        Interlocked.Increment(ref _activeEnqueues);
        try
        {
            if (Volatile.Read(ref _completed) != 0 || Volatile.Read(ref _doomed) != 0 || Volatile.Read(ref _faulted) != 0)
            {
                packet.Release();
                return false;
            }

            int length = packet.PayloadLength;
            if (Interlocked.Add(ref _pendingBytes, length) > _maxPendingBytes)
            {
                // Never a drop: past the cap the connection is closed as too slow. This packet and every later one are
                // released at once, and the owner sends the notice.
                Interlocked.Add(ref _pendingBytes, -length);
                packet.Release();
                Doom(SlowKickReason.Bytes);
                return false;
            }

            _queue.Enqueue(packet);
        }
        finally
        {
            Interlocked.Decrement(ref _activeEnqueues);
        }

        MarkDirty();
        return true;
    }

    /// <summary>
    /// Queues a time-sync ping. Its server timestamp is taken by the owner send thread as the ping's burst is written
    /// (#875), so the round trip a pong reports holds no queueing or tick wait. A second ping queued before the first
    /// went out carries the latest values. A ping still queued when the connection closes, or found too slow, is dropped
    /// unstamped, and none follows a disconnect notice.
    /// </summary>
    public void EnqueuePing(long clientTicks, long roundTrip, long offset)
    {
        Volatile.Write(ref _pingClientTicks, clientTicks);
        Volatile.Write(ref _pingRoundTrip, roundTrip);
        Volatile.Write(ref _pingOffset, offset);
        Enqueue(s_pingMarker);
    }

    /// <summary>
    /// Marks the connection too slow (once) and wakes its owner, which counts the kick, discards the queue and closes the
    /// connection. A connection already closing is not a kick: its close has its own budget.
    /// </summary>
    internal void Doom(SlowKickReason reason)
    {
        if (Volatile.Read(ref _completed) != 0 || Interlocked.CompareExchange(ref _doomed, (int)reason, 0) != 0)
            return;

        MarkDirty();
    }

    /// <summary>
    /// Closes the queue and waits, within <see cref="CloseBudget" />, for the owner to write what is left (a disconnect
    /// notice sent just before the close goes last) and finish. Past the budget the write in flight is cancelled and
    /// given <see cref="CancelGrace" /> to unwind; the connection then closes its socket under whatever is left.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0)
            return;

        // An enqueue that read "open" before the line above finishes pushing its packet; any later one sees the close
        // and releases its own. After this, nothing new joins the queue.
        var spin = new SpinWait();
        while (Volatile.Read(ref _activeEnqueues) != 0)
            spin.SpinOnce();

        MarkDirty();

        Task<bool> finishing = WithinAsync(_finished.Task, CloseBudget);
        if (finishing.IsCompleted)
        {
            // The owner finished this connection before the wait began, so this close would complete inline and its
            // caller would close the socket and run its close handler on the calling thread, the tick's among them. A
            // close is rare; one yield moves the rest off that thread.
            await Task.Yield();
        }

        if (!await finishing.ConfigureAwait(false))
        {
            // A peer that stopped reading cannot hold the close open.
            await _cts.CancelAsync().ConfigureAwait(false);
            await WithinAsync(_finished.Task, CancelGrace).ConfigureAwait(false);
        }

        if (_finished.Task.IsCompleted)
            _cts.Dispose();
    }

    /// <summary>One visit of the owner send thread: write what is queued, or finish a closed connection.</summary>
    internal void Service()
    {
        // A full fence, not a release store: the queue is read below, and a load moved above a plain store would miss a
        // packet whose enqueue then saw the flag still set and pushed nothing (Review Focus 4).
        Interlocked.Exchange(ref _dirty, 0);
        if (_finishedFlag)
        {
            ReleaseQueued();
            return;
        }

        // Before the write-in-flight check: a doomed connection gives back what it queued even while a write it will never
        // finish holds the stream, and later visits release what enqueues that raced the doom still pushed.
        if (Volatile.Read(ref _doomed) is var reason and not 0)
        {
            ServiceDoomed((SlowKickReason)reason);
            return;
        }

        // A write in flight marks this connection dirty again when it ends, and the rest goes then. Read before the fault
        // flag: a write that fails sets that flag before it clears this one, so a visit that reads no write in flight
        // also reads the fault, and never starts another write on a stream that has failed.
        if (IsWriteInFlight)
            return;

        if (Volatile.Read(ref _faulted) != 0)
        {
            ReleaseQueued();
            FinishIfIdle();
            return;
        }

        // Never connected: nothing can be written, but a close still finishes and gives back what was queued.
        if (_stream is null)
        {
            FinishIfIdle();
            return;
        }

        _burst.Reset();
        int packets = 0;
        int payloadBytes = 0;
        int length = 0;
        bool ping = false;
        try
        {
            while (_queue.TryDequeue(out OutboundPacket packet))
            {
                if (packet.Payload is null)
                {
                    ping = true; // the ping's marker: the ping itself goes last, below
                    continue;
                }

                if (packet.Header.Type == NetworkPacketType.SMSG_DISCONNECT)
                    _noticeWritten = true;

                length = packet.PayloadLength;
                try
                {
                    PacketEnvelope.Append(_burst, packet, _sealer);
                }
                finally
                {
                    packet.Release();
                }

                payloadBytes += length;
                length = 0;
                packets++;
            }

            // Stamped as the burst leaves, just before its write; never once the close began or a disconnect notice went
            // out: the notice stays last. Clear text, and outside the pending bytes: its marker carried none.
            if (ping && Volatile.Read(ref _completed) == 0 && !_noticeWritten)
            {
                long serverTicks = _time.GetUtcNow().UtcTicks;
                OutboundPacket stamped = SPingPacket.Create(serverTicks, Volatile.Read(ref _pingClientTicks),
                    Volatile.Read(ref _pingRoundTrip), Volatile.Read(ref _pingOffset), _encoder);
                try
                {
                    PacketEnvelope.Append(_burst, stamped, _sealer);
                }
                finally
                {
                    stamped.Release();
                }

                Interlocked.Exchange(ref _lastPingServerTicks, serverTicks);
                packets++;
            }
        }
        catch (Exception e)
        {
            // A packet that cannot be sealed (a session that never completed its exchange, or spent its counter, #855):
            // the burst is discarded and the connection closes. Going on would send what follows out of order, and spend
            // nonces, under a session that has already failed.
            Interlocked.Add(ref _pendingBytes, -(payloadBytes + length));
            Fault(e);
            return;
        }

        if (packets == 0)
        {
            FinishIfIdle();
            return;
        }

        _burstPayloadBytes = payloadBytes;
        StartWrite(packets);
    }

    /// <summary>
    /// The owner's visit to a connection found too slow: what is queued is released, never written. The first visit sends
    /// the notice, past the byte cap and with no write in flight, and requests the close; the visit after the close
    /// finishes it once no write is in flight.
    /// </summary>
    private void ServiceDoomed(SlowKickReason reason)
    {
        ReleaseQueued();
        if (!_doomHandled)
        {
            _doomHandled = true;
            // Counted here, once and on the owner thread, never on the enqueuing thread (often the tick).
            _scheduler.Metrics.SlowKick(reason);
            _logger.LogInformation("Closing connection {Id}: it reads too slowly ({Reason})", ConnectionId, reason);

            // Past the byte cap the notice is its only packet, when nothing is still being written. A stalled write gets
            // none: a peer that is not reading the last burst would not read the notice either. The in-flight flag is
            // read before the fault flag, as in Service: a write that just failed is never followed by the notice.
            if (reason == SlowKickReason.Bytes && _stream is not null && !IsWriteInFlight && Volatile.Read(ref _faulted) == 0)
            {
                _burst.Reset();
                OutboundPacket notice = SDisconnectPacket.Create(SlowConnectionMessage, DisconnectReason.SlowConnection, _encoder);
                try
                {
                    PacketEnvelope.Append(_burst, notice, _sealer);
                }
                finally
                {
                    notice.Release();
                }

                _burstPayloadBytes = 0;
                StartWrite(packets: 1);
            }

            RequestClose();
        }

        FinishIfIdle();
    }

    /// <summary>A failure inside the owner's pass (a payload that cannot be sealed): this connection only is closed.</summary>
    internal void Fault(Exception error)
    {
        _logger.LogError(error, "Send failed for connection {Id}; closing it", ConnectionId);
        Volatile.Write(ref _faulted, 1);
        ReleaseQueued();
        RequestClose();
        FinishIfIdle();
    }

    private void StartWrite(int packets)
    {
        _scheduler.Metrics.Burst(packets, _burst.Written);
        Volatile.Write(ref _writeStarted, _time.GetTimestamp());
        Volatile.Write(ref _writeInFlight, 1);

        ValueTask write;
        try
        {
            write = _stream!.WriteAsync(_burst.WrittenMemory, _cts.Token);
        }
        catch (Exception e)
        {
            WriteEndedInline(e);
            return;
        }

        if (write.IsCompleted)
        {
            Exception? error = null;
            try
            {
                // A send thread never awaits: the outcome of a write that already ended is read here.
#pragma warning disable MA0045
                write.GetAwaiter().GetResult();
#pragma warning restore MA0045
            }
            catch (Exception e)
            {
                error = e;
            }

            WriteEndedInline(error);
            return;
        }

        // The socket's buffer is full: the write ends where it completes, and the owner reads its stall clock meanwhile.
        _scheduler.TrackWriting(this);
        _pendingWrite = write.ConfigureAwait(false).GetAwaiter();
        _pendingWrite.UnsafeOnCompleted(_onPendingWriteCompleted);
    }

    private void OnPendingWriteCompleted()
    {
        Exception? error = null;
        try
        {
            // The write's own completion: its outcome is ready, and reading it is all this callback waits for.
#pragma warning disable MA0045
            _pendingWrite.GetResult();
#pragma warning restore MA0045
        }
        catch (Exception e)
        {
            error = e;
        }

        _pendingWrite = default;
        WriteEnded(error);

        // What was queued meanwhile, a close waiting for the queue to empty, or a fault's leftovers: the owner's next
        // visit. After the in-flight flag cleared, so that visit can start the next write.
        MarkDirty();
    }

    /// <summary>
    /// A write that ended inside <see cref="StartWrite" />, on the owner thread: it is not marked dirty again, which
    /// would wake this thread for a burst of one or two packets and split the tick's sends into several records. What
    /// was enqueued, or a close requested, since this visit cleared the dirty flag pushed the connection itself.
    /// </summary>
    private void WriteEndedInline(Exception? error)
    {
        WriteEnded(error);
        if (error is not null)
        {
            ReleaseQueued();
            FinishIfIdle();
        }
        else if (Volatile.Read(ref _completed) != 0)
        {
            // Closing: one more visit writes what was enqueued after this burst was drained, or finishes. It cannot finish
            // here, since an enqueue that began before the close may still be adding the last packet; and the close's
            // own mark may have found the connection already listed, for this visit, and pushed nothing.
            MarkDirty();
        }
    }

    private void WriteEnded(Exception? error)
    {
        Interlocked.Add(ref _pendingBytes, -_burstPayloadBytes);

        if (error is not null)
        {
            // Any error ends the stream for good, a cancellation too: a cancelled TLS write leaves it unusable. Set before
            // the in-flight flag clears, so the owner's next visit cannot start another write on it.
            Volatile.Write(ref _faulted, 1);
        }

        Volatile.Write(ref _writeInFlight, 0);

        if (error is not null && !(error is OperationCanceledException && _cts.IsCancellationRequested))
        {
            if (error is IOException or SocketException or ObjectDisposedException)
                _logger.LogDebug(error, "Write failed for connection {Id}; closing", ConnectionId);
            else
                _logger.LogError(error, "Write faulted for connection {Id}; closing", ConnectionId);
            RequestClose();
        }
    }

    private void MarkDirty()
    {
        if (Interlocked.CompareExchange(ref _dirty, 1, 0) == 0)
            _scheduler.MarkDirty(this);
        else
            _scheduler.Wake(this); // already listed, perhaps by a tick send still waiting for the tick's signal
    }

    private void RequestClose()
    {
        if (Interlocked.Exchange(ref _closeRequested, 1) != 0)
            return;

        try
        {
            _close();
        }
        catch (Exception e)
        {
            // It runs on a send thread or where a write completed: a throw would end the pass, or the process.
            _logger.LogCritical(e, "Closing connection {Id} after a send failure threw; the connection may stay open", ConnectionId);
        }
    }

    private void FinishIfIdle()
    {
        if (Volatile.Read(ref _completed) != 0 && !IsWriteInFlight)
            Finish();
    }

    private void Finish()
    {
        if (_finishedFlag)
            return;

        _finishedFlag = true;
        ReleaseQueued();
        // No write reads the burst any more: it goes back to the array pool.
        _burst.Dispose();
        _finished.TrySetResult();
    }

    private void ReleaseQueued()
    {
        while (_queue.TryDequeue(out OutboundPacket packet))
        {
            Interlocked.Add(ref _pendingBytes, -packet.PayloadLength);
            packet.Release();
        }
    }

    private static async Task<bool> WithinAsync(Task task, TimeSpan budget)
    {
        if (task.IsCompleted)
            return true;

        // A shutdown-timeout Delay: it bounds the close, so a peer that stopped reading cannot hold it open.
        await Task.WhenAny(task, Task.Delay(budget, CancellationToken.None)).ConfigureAwait(false);
        return task.IsCompleted;
    }
}
