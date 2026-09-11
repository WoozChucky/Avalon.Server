// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using System;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using Microsoft.Extensions.Logging;

namespace Avalon.Hosting.Networking;

public sealed class ChannelOutbox : IOutbox
{
    private readonly Channel<NetworkPacket> _queue;
    private readonly PooledArrayBufferWriter _burstWriter = new();
    private readonly PooledArrayBufferWriter _tempWriter = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger _logger;
    private readonly Guid _connectionId;
    private PacketStream? _stream;
    private Task? _bgTask;

    public static readonly TimeSpan DefaultFlushTimeout = TimeSpan.FromMilliseconds(500);

    // After the flush budget the write is cancelled; this is how long the loop is given to
    // unwind and stop naming the pooled buffers.
    public static readonly TimeSpan DefaultCancelGrace = TimeSpan.FromMilliseconds(100);

    private readonly TimeSpan _flushTimeout;
    private readonly TimeSpan _cancelGrace;

    /// <remarks>
    /// The two budgets are settable so a test can bound itself against the value it passed in
    /// rather than against a wall clock it does not control. Production leaves them alone.
    /// </remarks>
    public ChannelOutbox(Guid connectionId, ILogger logger, int capacity,
        TimeSpan? flushTimeout = null, TimeSpan? cancelGrace = null)
    {
        _connectionId = connectionId;
        _logger = logger;
        _flushTimeout = flushTimeout ?? DefaultFlushTimeout;
        _cancelGrace = cancelGrace ?? DefaultCancelGrace;
        _queue = Channel.CreateBounded<NetworkPacket>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public void Connect(PacketStream stream)
    {
        _stream = stream;
        // The drain task itself is kept, not the fault continuation: disposal waits on this
        // to know the queue has been written out.
        _bgTask = Task.Factory.StartNew(
                DrainLoop,
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default)
            .Unwrap();

        _ = _bgTask.ContinueWith(
            t => _logger.LogError(t.Exception, "Send loop faulted for connection {Id}", _connectionId),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    public bool Enqueue(NetworkPacket packet)
    {
        if (!_queue.Writer.TryWrite(packet))
        {
            // A full queue drops its oldest entry and takes this one, so a refusal means the
            // outbox is closing and this packet has missed it.
            _logger.LogDebug("Outbox closed for connection {Id}; dropped {Type}", _connectionId, packet.Header.Type);
            return false;
        }
        return true;
    }

    public void Flush() { } // bg drain loop handles writes; no tick-driven flush needed

    public async ValueTask DisposeAsync()
    {
        // Closing means: stop accepting new packets, deliver what is already queued, then go.
        // Completing the writer is what ends the drain loop — cancelling first would abort the
        // write it is sitting on, and a packet the peer never receives looks like a dropped
        // connection rather than the reason it was closed.
        _queue.Writer.TryComplete();

#pragma warning disable MA0040 // shutdown-timeout Delays — they bound the teardown, so a peer that stopped reading cannot hold the close open
        if (_bgTask is not null)
            await Task.WhenAny(_bgTask, Task.Delay(_flushTimeout)).ConfigureAwait(false);

        // Past the budget the remaining writes are abandoned.
        await _cts.CancelAsync().ConfigureAwait(false);

        // A cancelled write is still holding the burst buffer when the cancel lands, so give
        // it a moment to unwind before that buffer is handed back.
        if (_bgTask is not null)
            await Task.WhenAny(_bgTask, Task.Delay(_cancelGrace)).ConfigureAwait(false);
#pragma warning restore MA0040

        // The writers rent from ArrayPool and the loop still names the cancellation source, so
        // release neither while a write that ignored the cancel could still be reading out of
        // them: whichever connection rents that array next would put these bytes on its own
        // socket. A rental that is dropped instead of returned is just collected.
        if (_bgTask is null || _bgTask.IsCompleted)
        {
            _burstWriter.Dispose();
            _tempWriter.Dispose();
            _cts.Dispose();
        }
    }

    private async Task DrainLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                NetworkPacket packet = await _queue.Reader.ReadAsync(_cts.Token).ConfigureAwait(false);
                try
                {
                    if (_stream is null)
                    {
                        _logger.LogCritical("Stream unexpectedly null in ChannelOutbox {Id}", _connectionId);
                        break;
                    }

                    _burstWriter.Reset();
                    do
                    {
                        OutboxSerializer.AppendPacket(_burstWriter, _tempWriter, packet);
                        if (_logger.IsEnabled(LogLevel.Trace) &&
                            packet.Header.Type != NetworkPacketType.SMSG_WORLD_STATE_UPDATE &&
                            packet.Header.Type != NetworkPacketType.SMSG_PING)
                        {
                            _logger.LogTrace("OUT: {Type} => {Packet}", packet.Header.Type,
                                JsonSerializer.Serialize(packet));
                        }
                    } while (_queue.Reader.TryRead(out packet));

                    await _stream.WriteAsync(_burstWriter.WrittenMemory, _cts.Token).ConfigureAwait(false);
                }
                catch (SocketException e) when (e.SocketErrorCode == SocketError.ConnectionReset)
                {
                    break;
                }
                catch (OperationCanceledException) when (_cts.IsCancellationRequested)
                {
                    // The flush budget expired. Deliberate, and not a send failure.
                    break;
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Failed to send packet for connection {Id}", _connectionId);
                }
            }
            catch (ChannelClosedException)
            {
                // Queue completed and drained: the normal end of the loop.
                break;
            }
            catch (SocketException)
            {
                break;
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException e)
            {
                _logger.LogError(e, "Unexpected cancellation in send loop for connection {Id}", _connectionId);
                break;
            }
        }
    }
}
