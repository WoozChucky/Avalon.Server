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

    private static readonly TimeSpan FlushTimeout = TimeSpan.FromMilliseconds(500);

    public ChannelOutbox(Guid connectionId, ILogger logger, int capacity)
    {
        _connectionId = connectionId;
        _logger = logger;
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
            _logger.LogWarning("Send buffer full for connection {Id}; dropped {Type}", _connectionId, packet.Header.Type);
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

        if (_bgTask is not null)
#pragma warning disable MA0040 // shutdown-timeout Delay — bounds the flush, so a peer that stopped reading cannot hold the close open
            await Task.WhenAny(_bgTask, Task.Delay(FlushTimeout)).ConfigureAwait(false);
#pragma warning restore MA0040

        // Past the budget the remaining writes are abandoned.
        await _cts.CancelAsync().ConfigureAwait(false);

        _burstWriter.Dispose();
        _tempWriter.Dispose();
        _cts.Dispose();
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
