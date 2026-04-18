using System;
using System.Buffers;
using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Avalon.Common.Cryptography;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProtoBuf;

namespace Avalon.Hosting.Networking;

public interface IConnection
{
    Guid Id { get; }
    Task? ExecuteTask { get; }
    public string RemoteEndPoint { get; }
    public IAvalonCryptoSession CryptoSession { get; }
    public ICryptoManager ServerCrypto { get; }

    void Close(bool expected = true);
    void Send(NetworkPacket packet);
    Task StartAsync(CancellationToken token = default);
}

public abstract class Connection : BackgroundService, IConnection
{
    protected readonly ILogger _logger;
    private readonly IPacketReader _packetReader;

    private readonly Channel<NetworkPacket> _channel;

    // Timer for calculating rates every second
    private readonly Timer _rateCalculationTimer;

    protected readonly IServerBase Server;

    private TcpClient? _client;
    private bool _closed;
    private PacketStream? _stream;
    private readonly PooledArrayBufferWriter _tempWriter = new();
    private readonly PooledArrayBufferWriter _burstWriter = new();
    protected long BytesReceivedCount;
    protected double BytesReceivedRate;
    protected long BytesSentCount;
    protected double BytesSentRate;
    protected CancellationTokenSource? CancellationTokenSource;
    protected int PacketReceivedCount;
    protected double PacketReceivedRate;

    // Accumulators for packets and bytes
    protected int PacketSentCount;
    protected double PacketSentRate;

    protected Connection(ILogger logger, IServerBase server, IPacketReader packetReader)
    {
        _logger = logger;
        _packetReader = packetReader;
        Server = server;
        CryptoSession = new AvalonCryptoSession(ServerCrypto.GetKeyPair());
        Id = Guid.NewGuid();
        _channel = Channel.CreateBounded<NetworkPacket>(new BoundedChannelOptions(server.SendBufferCapacity)
        {
            FullMode     = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        // Start a timer to calculate and log rates every second
        _rateCalculationTimer = new Timer(CalculateAndLogRates, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }

    protected bool IsConnected => _client?.Connected == true;
    public Guid Id { get; }
    public string RemoteEndPoint { get; private set; }
    public IAvalonCryptoSession CryptoSession { get; }
    public ICryptoManager ServerCrypto => Server.Crypto;

    public void Close(bool expected = true)
    {
        if (_closed)
        {
            return;
        }

        CancellationTokenSource?.Cancel();
        _channel.Writer.TryComplete();
        _client?.Close();
        _closed = true;
        OnClose(expected);
    }

    public virtual void Send(NetworkPacket packet)
    {
        if (!_channel.Writer.TryWrite(packet))
            _logger.LogWarning("Send buffer full for connection {Id}; dropped {Type}", Id, packet.Header.Type);
        else
        {
            Interlocked.Add(ref BytesSentCount, packet.Size);
            Interlocked.Increment(ref PacketSentCount);
        }
    }

    protected void Init(TcpClient client)
    {
        _client = client;
        RemoteEndPoint = client.Client.RemoteEndPoint?.ToString() ?? "Unknown";
        CancellationTokenSource = new CancellationTokenSource();
        _ = Task.Factory.StartNew(
                SendPacketsWhenAvailable,
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default)
            .Unwrap()
            .ContinueWith(
                t => _logger.LogError(t.Exception, "Send loop faulted for connection {Id}", Id),
                TaskContinuationOptions.OnlyOnFaulted);
    }

    protected abstract void OnHandshakeFinished();

    protected abstract Task<PacketStream> GetStream(TcpClient client);

    protected abstract Task OnClose(bool expected = true);

    protected abstract Task OnReceive(NetworkPacketHeader header, Packet? payload);

    protected virtual void OnPacketAccounted(int size) { }

    protected abstract long GetServerTime();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_client is null)
        {
            _logger.LogCritical("Cannot execute when client is null");
            return;
        }

        _logger.LogInformation("New connection from {RemoteEndPoint}", _client.Client.RemoteEndPoint?.ToString());

        _stream = await GetStream(_client);

        OnHandshakeFinished();

        try
        {
            await foreach (InboundPacketFrame frame in _packetReader.EnumerateAsync(_stream, stoppingToken))
            {
                if (_logger.IsEnabled(LogLevel.Debug) &&
                    frame.Header.Type != NetworkPacketType.CMSG_MOVEMENT &&
                    frame.Header.Type != NetworkPacketType.CMSG_PONG)
                {
                    _logger.LogDebug("IN: {Type}", frame.Header.Type);
                }

                Packet? payload = _packetReader.Read(
                    frame,
                    frame.Header.Flags.HasFlag(NetworkPacketFlags.Encrypted) ? CryptoSession.Decrypt : null);

                // Accumulate for rate calculation
                int packetSize = frame.Size;
                Interlocked.Add(ref BytesReceivedCount, packetSize);
                Interlocked.Increment(ref PacketReceivedCount);
                OnPacketAccounted(packetSize);

                await OnReceive(frame.Header, payload);
            }
        }
        catch (IOException e)
        {
            _logger.LogDebug(e, "Connection was closed. Probably by the other party");
            Close(false);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to read from stream");
            Close(false);
        }

        Close(false);
    }

    private void CalculateAndLogRates(object? state)
    {
        // Calculate rates for packets and bytes sent and received
        PacketSentRate = PacketSentCount / 1.0; // packets per second
        PacketReceivedRate = PacketReceivedCount / 1.0; // packets per second
        BytesSentRate = BytesSentCount / 1.0; // bytes per second
        BytesReceivedRate = BytesReceivedCount / 1.0; // bytes per second

        // Reset accumulators for the next interval
        Interlocked.Exchange(ref PacketSentCount, 0);
        Interlocked.Exchange(ref PacketReceivedCount, 0);
        Interlocked.Exchange(ref BytesSentCount, 0);
        Interlocked.Exchange(ref BytesReceivedCount, 0);
    }

    private async Task SendPacketsWhenAvailable()
    {
        if (_client?.Connected != true)
        {
            _logger.LogWarning("Tried to send data to a closed connection");
            return;
        }

        while (CancellationTokenSource?.IsCancellationRequested != true)
        {
            try
            {
                NetworkPacket packet =
                    await _channel.Reader.ReadAsync(CancellationTokenSource!.Token).ConfigureAwait(false);

                try
                {
                    if (_stream is null || _client?.Connected != true)
                    {
                        CancellationTokenSource?.Cancel();
                        _logger.LogCritical("Stream unexpectedly became null or client disconnected. This shouldn't happen");
                        break;
                    }

                    _burstWriter.Reset();
                    do
                    {
                        AppendPacket(packet);

                        if (_logger.IsEnabled(LogLevel.Trace) &&
                            packet.Header.Type != NetworkPacketType.SMSG_WORLD_STATE_UPDATE &&
                            packet.Header.Type != NetworkPacketType.SMSG_PING)
                        {
                            _logger.LogTrace("OUT: {Type} => {Packet}", packet.Header.Type,
                                JsonSerializer.Serialize(packet));
                        }
                    } while (_channel.Reader.TryRead(out packet));

                    await _stream.WriteAsync(_burstWriter.WrittenMemory,
                        CancellationTokenSource!.Token).ConfigureAwait(false);
                    await _stream.FlushAsync(CancellationTokenSource!.Token).ConfigureAwait(false);
                }
                catch (SocketException e)
                {
                    if (e.SocketErrorCode == SocketError.ConnectionReset)
                    {
                        break;
                    }

                    _logger.LogError(e, "Failed to send packet");
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Failed to send packet");
                }
            }
            catch (SocketException)
            {
                break;
            }
            catch (OperationCanceledException) when (CancellationTokenSource?.IsCancellationRequested == true)
            {
                // Expected: Close() cancelled the token. Exit cleanly.
                _logger.LogDebug("Send loop stopping for connection {Id}", Id);
                break;
            }
            catch (OperationCanceledException e)
            {
                // Unexpected: some other cancellation source fired — always a bug.
                _logger.LogError(e, "Send loop for connection {Id} received unexpected cancellation", Id);
                break;
            }
        }
    }

    private void AppendPacket(NetworkPacket packet)
    {
        _tempWriter.Reset();
        Serializer.Serialize(_tempWriter, packet);

        WriteVarint(_burstWriter, (uint)_tempWriter.Written);
        var dest = _burstWriter.GetSpan(_tempWriter.Written);
        _tempWriter.WrittenSpan.CopyTo(dest);
        _burstWriter.Advance(_tempWriter.Written);
    }

    private static void WriteVarint(IBufferWriter<byte> writer, uint value)
    {
        Span<byte> span = writer.GetSpan(5);
        int i = 0;
        while (value > 0x7F) { span[i++] = (byte)((value & 0x7F) | 0x80); value >>= 7; }
        span[i++] = (byte)value;
        writer.Advance(i);
    }
}
