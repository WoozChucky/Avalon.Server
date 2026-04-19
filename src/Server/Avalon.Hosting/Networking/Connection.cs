// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Avalon.Common.Cryptography;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

    protected readonly IServerBase Server;

    private TcpClient? _client;
    private int _closed; // 0 = open, 1 = closed
    private PacketStream? _stream;

    protected IOutbox? _outbox;

    protected long BytesReceivedCount;
    protected double BytesReceivedRate;
    protected long BytesSentCount;
    protected double BytesSentRate;
    protected int PacketReceivedCount;
    protected double PacketReceivedRate;
    protected int PacketSentCount;
    protected double PacketSentRate;

    protected Connection(ILogger logger, IServerBase server, IPacketReader packetReader)
    {
        _logger = logger;
        _packetReader = packetReader;
        Server = server;
        CryptoSession = new AvalonCryptoSession(ServerCrypto.GetKeyPair());
        Id = Guid.NewGuid();
    }

    protected bool IsConnected => _client?.Connected == true;
    public Guid Id { get; }
    public string RemoteEndPoint { get; private set; } = "Unknown";
    public IAvalonCryptoSession CryptoSession { get; }
    public ICryptoManager ServerCrypto => Server.Crypto;

    public void Close(bool expected = true)
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        _ = _outbox?.DisposeAsync().AsTask();
        _client?.Close();
        OnClose(expected);
    }

    public virtual void Send(NetworkPacket packet)
    {
        if (_outbox is null) return;
        if (!_outbox.Enqueue(packet)) return;
        Interlocked.Add(ref BytesSentCount, packet.Size);
        Interlocked.Increment(ref PacketSentCount);
    }

    protected void Init(TcpClient client)
    {
        _client = client;
        RemoteEndPoint = client.Client.RemoteEndPoint?.ToString() ?? "Unknown";
    }

    protected virtual IOutbox OnCreateOutbox() =>
        new ChannelOutbox(Id, _logger, Server.SendBufferCapacity);

    protected abstract void OnHandshakeFinished();

    protected abstract Task<PacketStream> GetStream(TcpClient client);

    protected abstract Task OnClose(bool expected = true);

    protected abstract ValueTask OnReceive(NetworkPacketHeader header, Packet? payload);

    protected virtual void OnPacketAccounted(NetworkPacketType type, int size) { }

    protected abstract long GetServerTime();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (_client is null)
            {
                _logger.LogCritical("Cannot execute when client is null");
                return;
            }

            _logger.LogInformation("New connection from {RemoteEndPoint}", _client.Client.RemoteEndPoint?.ToString());

            _stream = await GetStream(_client);

            _outbox = OnCreateOutbox();
            _outbox.Connect(_stream);

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

                    int packetSize = frame.Size;
                    Interlocked.Add(ref BytesReceivedCount, packetSize);
                    Interlocked.Increment(ref PacketReceivedCount);
                    OnPacketAccounted(frame.Header.Type, packetSize);

                    ValueTask receiveTask = OnReceive(frame.Header, payload);
                    if (!receiveTask.IsCompletedSuccessfully)
                        await receiveTask.ConfigureAwait(false);
                }
            }
            catch (IOException e)
            {
                _logger.LogDebug(e, "Connection was closed. Probably by the other party");
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to read from stream");
            }
        }
        finally
        {
            Close(false);
        }
    }
}
