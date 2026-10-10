using System.Diagnostics.Metrics;
using System.Net.Sockets;
using System.Threading.Channels;
using Avalon.Common.Cryptography;
using Avalon.Hosting.Telemetry;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using Microsoft.Extensions.Logging;

namespace Avalon.Hosting.Networking;

public sealed class TickDrivenOutbox : IOutbox
{
    private readonly Channel<OutboundPacket> _queue;
    private readonly PooledArrayBufferWriter _burstWriter = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger _logger;
    private readonly Guid _connectionId;
    private readonly Action _onFault;
    private readonly IAvalonCryptoSession? _sealer;

    // 0 = idle, 1 = write in flight. Set by Flush via CompareExchange; cleared on success.
    // Stays at 1 after fault — connection is dead, no more writes.
    private int _writeInFlight;

    // 1 once a write has ended without clearing the flag above — faulted or cancelled. The write
    // task is over either way, so nothing is reading the buffers even though the slot stays taken.
    private int _writeSettled;

    private volatile TaskCompletionSource? _inFlightCompletion;

    private PacketStream? _stream;

    private static readonly Action<Task, object?> s_onWriteCompleted = OnWriteCompleted;

    public static readonly TimeSpan DefaultFlushTimeout = TimeSpan.FromMilliseconds(500);

    // After the flush budget the write is cancelled; this is how long it is given to unwind
    // and stop naming the pooled buffers.
    public static readonly TimeSpan DefaultCancelGrace = TimeSpan.FromMilliseconds(100);

    private readonly TimeSpan _flushTimeout;
    private readonly TimeSpan _cancelGrace;

    /// <remarks>
    /// The two budgets are settable so a test can bound itself against the value it passed in
    /// rather than against a wall clock it does not control. Production leaves them alone.
    /// </remarks>
    /// <param name="dropped">
    /// Counts the packets a full outbox evicts, oldest first, tagged by packet type. Only those
    /// capacity evictions are counted: an <see cref="Enqueue"/> refused because the outbox is
    /// closed is not a drop.
    /// </param>
    /// <param name="sealer">
    /// Seals each packet flagged Encrypted as the flush frames it; with none, every packet goes plain (inside TLS).
    /// </param>
    public TickDrivenOutbox(Guid connectionId, ILogger logger, int capacity, Action onFault,
        IAvalonCryptoSession? sealer = null, TimeSpan? flushTimeout = null, TimeSpan? cancelGrace = null,
        Counter<long>? dropped = null)
    {
        _connectionId = connectionId;
        _logger = logger;
        _onFault = onFault;
        _sealer = sealer;
        _flushTimeout = flushTimeout ?? DefaultFlushTimeout;
        _cancelGrace = cancelGrace ?? DefaultCancelGrace;
        _queue = Channel.CreateBounded<OutboundPacket>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            // The flush reads, and so does Dispose's release loop, which can run while a flush still does.
            SingleReader = false,
            SingleWriter = false
        }, packet =>
        {
            packet.Release();
            dropped?.Add(1, new KeyValuePair<string, object?>("avalon.packet.type",
                PacketDispatchTelemetry.NameOf(packet.Header.Type)));
        });
    }

    public void Connect(PacketStream stream) => _stream = stream;

    public bool Enqueue(OutboundPacket packet)
    {
        if (!_queue.Writer.TryWrite(packet))
        {
            // A full queue drops its oldest entry and takes this one, so a refusal means the
            // outbox is closing and this packet has missed it.
            _logger.LogDebug("Outbox closed for connection {Id}; dropped {Type}", _connectionId, packet.Header.Type);
            packet.Release();
            return false;
        }
        return true;
    }

    public void Flush()
    {
        if (_stream is null) return;

        // Skip-and-coalesce: leave packets in queue for next tick if a write is in flight.
        // Prevents concurrent socket writes (protocol corruption) without blocking the tick thread.
        if (Interlocked.CompareExchange(ref _writeInFlight, 1, 0) != 0) return;

        Volatile.Write(ref _writeSettled, 0);

        _burstWriter.Reset();
        int count = 0;
        try
        {
            while (_queue.Reader.TryRead(out OutboundPacket packet))
            {
                try
                {
                    PacketEnvelope.Append(_burstWriter, packet, _sealer);
                }
                finally
                {
                    packet.Release();
                }

                count++;
            }
        }
        catch (Exception e)
        {
            // A packet that cannot be sealed (a session that never completed its exchange, or spent its counter, #855)
            // closes this connection only: the tick goes on to flush every other one.
            _logger.LogError(e, "Could not frame a packet for connection {Id}; closing", _connectionId);
            while (_queue.Reader.TryRead(out OutboundPacket left))
                left.Release();
            Volatile.Write(ref _writeInFlight, 0);
            _onFault();
            return;
        }

        if (count == 0)
        {
            Volatile.Write(ref _writeInFlight, 0);
            return;
        }

        ValueTask write = _stream.WriteAsync(_burstWriter.WrittenMemory, _cts.Token);

        // The usual case: a socket with room in its send buffer takes the bytes before WriteAsync returns. Its outcome
        // is read here, on the tick, because a continuation on a write that is already over costs a task per flush,
        // per connection, every tick (#875); what a continuation would do on success is all that happens.
        if (write.IsCompletedSuccessfully)
        {
            write.GetAwaiter().GetResult();
            OnWriteSucceeded();
            return;
        }

        // Still writing, or already failed. ExecuteSynchronously: a write that has already failed is handled inline on
        // the tick thread; one still in flight is handled where it completes (one thread-pool work item for a socket).
        // The continuation observes the write's outcome; its own task is discarded, since OnWriteCompleted handles
        // every way the write ends.
        _ = write.AsTask()
            .ContinueWith(
                s_onWriteCompleted,
                this,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
    }

    private void OnWriteSucceeded()
    {
        Volatile.Write(ref _writeInFlight, 0);
        _inFlightCompletion?.TrySetResult();
    }

    private static void OnWriteCompleted(Task t, object? state)
    {
        var self = (TickDrivenOutbox)state!;

        // Record that the write is over before anything downstream can look: a fault closes the
        // connection from here, and the close has to be able to tell "finished badly" from
        // "still writing" — the buffers it releases are pooled.
        if (t.IsCompletedSuccessfully)
        {
            self.OnWriteSucceeded();
            return;
        }

        Volatile.Write(ref self._writeSettled, 1); // flag stays at 1 — dead connection; no further writes
        self._inFlightCompletion?.TrySetResult();

        // Only the close cancels a write (its budget ran out): the connection is already going, and there is no fault.
        if (t.IsCanceled) return;

        Exception e = t.Exception?.GetBaseException() ?? new InvalidOperationException("Unknown write fault");
        if (e is OperationCanceledException) return;

        if (e is IOException or SocketException)
            self._logger.LogDebug(e, "Outbox write failed for connection {Id}; closing", self._connectionId);
        else
            self._logger.LogError(e, "Outbox write faulted for connection {Id}; closing", self._connectionId);

        self._onFault();
    }

    public async ValueTask DisposeAsync()
    {
        // Closing means: stop accepting new packets, deliver what is already queued, then go.
        // Writes here are tick-driven, so the tick that would have carried out whatever is still
        // queued may never come: this close is the last flush.
        _queue.Writer.TryComplete();

        bool idle = await DeliverRemainingAsync().ConfigureAwait(false);

        // Past the budget the remaining write is abandoned.
        await _cts.CancelAsync().ConfigureAwait(false);

        // A cancelled write is still holding the burst buffer when the cancel lands, so give it
        // a moment to unwind before that buffer is handed back.
        if (!idle)
            idle = await WaitForWriteAsync(_cancelGrace).ConfigureAwait(false);

        // What the close could not deliver goes back to its pool. The queue is not what an in-flight write reads (that is
        // the burst buffer, below), so this holds whether or not the write has ended.
        while (_queue.Reader.TryRead(out OutboundPacket left))
            left.Release();

        // The writer rents from ArrayPool and the write still names the cancellation source, so
        // release neither while a write that ignored the cancel could still be reading out of
        // them: whichever connection rents that array next would put these bytes on its own
        // socket. A rental that is dropped instead of returned is just collected.
        if (idle)
        {
            _burstWriter.Dispose();
            _cts.Dispose();
        }
    }

    /// <summary>
    /// Writes out whatever the last tick left behind, within one budget shared by both waits.
    /// Returns true when nothing is left reading the pooled buffers.
    /// </summary>
    private async Task<bool> DeliverRemainingAsync()
    {
        long deadline = Environment.TickCount64 + (long)_flushTimeout.TotalMilliseconds;

        // Two concurrent writes to one socket interleave into corruption, so the last flush
        // cannot start until the one already in flight has landed.
        if (!await WaitForWriteAsync(RemainingUntil(deadline)).ConfigureAwait(false))
            return false;

        Flush();

        return await WaitForWriteAsync(RemainingUntil(deadline)).ConfigureAwait(false);
    }

    /// <summary>Returns true once no write is in flight, false if the budget ran out first.</summary>
    private async Task<bool> WaitForWriteAsync(TimeSpan budget)
    {
        if (IsWriteIdle()) return true;

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _inFlightCompletion = tcs;

        // Double-check after publishing: continuation may have already cleared the flag.
        if (IsWriteIdle()) return true;

#pragma warning disable MA0040 // shutdown-timeout Delay — bounds the teardown, so a peer that stopped reading cannot hold the close open
        await Task.WhenAny(tcs.Task, Task.Delay(budget)).ConfigureAwait(false);
#pragma warning restore MA0040
        return IsWriteIdle();
    }

    /// <summary>True when no write is reading the pooled buffers: none started, or the last one ended.</summary>
    private bool IsWriteIdle() =>
        Volatile.Read(ref _writeInFlight) == 0 || Volatile.Read(ref _writeSettled) == 1;

    private static TimeSpan RemainingUntil(long deadline) =>
        TimeSpan.FromMilliseconds(Math.Max(0L, deadline - Environment.TickCount64));
}
