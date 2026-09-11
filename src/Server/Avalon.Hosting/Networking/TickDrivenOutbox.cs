// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using Microsoft.Extensions.Logging;

namespace Avalon.Hosting.Networking;

public sealed class TickDrivenOutbox : IOutbox
{
    private readonly Channel<NetworkPacket> _queue;
    private readonly PooledArrayBufferWriter _burstWriter = new();
    private readonly PooledArrayBufferWriter _tempWriter = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger _logger;
    private readonly Guid _connectionId;
    private readonly Action _onFault;

    // 0 = idle, 1 = write in flight. Set by Flush via CompareExchange; cleared on success.
    // Stays at 1 after fault — connection is dead, no more writes.
    private int _writeInFlight;

    private volatile TaskCompletionSource? _inFlightCompletion;

    private PacketStream? _stream;

    private static readonly Action<Task, object?> s_onWriteCompleted = OnWriteCompleted;

    private static readonly TimeSpan s_flushTimeout = TimeSpan.FromMilliseconds(500);

    // After the flush budget the write is cancelled; this is how long it is given to unwind
    // and stop naming the pooled buffers.
    private static readonly TimeSpan s_cancelGrace = TimeSpan.FromMilliseconds(100);

    public TickDrivenOutbox(Guid connectionId, ILogger logger, int capacity, Action onFault)
    {
        _connectionId = connectionId;
        _logger = logger;
        _onFault = onFault;
        _queue = Channel.CreateBounded<NetworkPacket>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public void Connect(PacketStream stream) => _stream = stream;

    public bool Enqueue(NetworkPacket packet)
    {
        if (!_queue.Writer.TryWrite(packet))
        {
            _logger.LogWarning("Outbox full for connection {Id}; dropped {Type}", _connectionId, packet.Header.Type);
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

        _burstWriter.Reset();
        int count = 0;
        while (_queue.Reader.TryRead(out NetworkPacket packet))
        {
            OutboxSerializer.AppendPacket(_burstWriter, _tempWriter, packet);
            count++;
        }

        if (count == 0)
        {
            Volatile.Write(ref _writeInFlight, 0);
            return;
        }

        // ExecuteSynchronously: if WriteAsync completes synchronously (e.g., MemoryStream),
        // the continuation runs inline on the tick thread — zero TP hops.
        // For a real socket, async completion schedules exactly 1 WI.
        _stream.WriteAsync(_burstWriter.WrittenMemory, _cts.Token)
            .AsTask()
            .ContinueWith(
                s_onWriteCompleted,
                this,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
    }

    private static void OnWriteCompleted(Task t, object? state)
    {
        var self = (TickDrivenOutbox)state!;

        if (!t.IsCompletedSuccessfully)
        {
            Exception e = t.Exception?.GetBaseException() ?? new InvalidOperationException("Unknown write fault");
            if (e is not OperationCanceledException)
            {
                if (e is IOException or SocketException)
                    self._logger.LogDebug(e, "Outbox write failed for connection {Id}; closing", self._connectionId);
                else
                    self._logger.LogError(e, "Outbox write faulted for connection {Id}; closing", self._connectionId);

                self._inFlightCompletion?.TrySetResult();
                self._onFault();
            }
            // Flag stays at 1 — dead connection; no further writes.
            return;
        }

        Volatile.Write(ref self._writeInFlight, 0);
        self._inFlightCompletion?.TrySetResult();
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
            idle = await WaitForWriteAsync(s_cancelGrace).ConfigureAwait(false);

        // The writers rent from ArrayPool and the write still names the cancellation source, so
        // release neither while a write that ignored the cancel could still be reading out of
        // them: whichever connection rents that array next would put these bytes on its own
        // socket. A rental that is dropped instead of returned is just collected.
        if (idle)
        {
            _burstWriter.Dispose();
            _tempWriter.Dispose();
            _cts.Dispose();
        }
    }

    /// <summary>
    /// Writes out whatever the last tick left behind, within one budget shared by both waits.
    /// Returns true when nothing is left reading the pooled buffers.
    /// </summary>
    private async Task<bool> DeliverRemainingAsync()
    {
        long deadline = Environment.TickCount64 + (long)s_flushTimeout.TotalMilliseconds;

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
        if (Volatile.Read(ref _writeInFlight) == 0) return true;

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _inFlightCompletion = tcs;

        // Double-check after publishing: continuation may have already cleared the flag.
        if (Volatile.Read(ref _writeInFlight) == 0) return true;

#pragma warning disable MA0040 // shutdown-timeout Delay — bounds the teardown, so a peer that stopped reading cannot hold the close open
        await Task.WhenAny(tcs.Task, Task.Delay(budget)).ConfigureAwait(false);
#pragma warning restore MA0040
        return tcs.Task.IsCompleted;
    }

    private static TimeSpan RemainingUntil(long deadline) =>
        TimeSpan.FromMilliseconds(Math.Max(0L, deadline - Environment.TickCount64));
}
