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
        _bgTask = Task.Factory.StartNew(
                DrainLoop,
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default)
            .Unwrap()
            .ContinueWith(
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
        _queue.Writer.TryComplete();
        await _cts.CancelAsync().ConfigureAwait(false);
        if (_bgTask is not null)
#pragma warning disable MA0040 // shutdown-timeout Delay — must complete even though _cts is already cancelled
            await Task.WhenAny(_bgTask, Task.Delay(500)).ConfigureAwait(false);
#pragma warning restore MA0040
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
