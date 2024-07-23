using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalon.Common.Cryptography;
using Avalon.Common.Threading;
using Avalon.Hosting.Extensions;
using Avalon.Hosting.PluginTypes;
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
    public Guid Id { get; }
    public string RemoteEndPoint { get; private set; }
    public IAvalonCryptoSession CryptoSession { get; private set; }
    public ICryptoManager ServerCrypto  => Server.Crypto;

    protected readonly ILogger _logger;
    protected CancellationTokenSource? CancellationTokenSource;
    
    private readonly PluginExecutor _pluginExecutor;
    private readonly RingBuffer<NetworkPacket> _packetsToSend = new(string.Empty, 100);
    private readonly IPacketReader _packetReader;

    private TcpClient? _client;
    private Stream? _stream;
    private bool _closed = false;
    
    protected readonly IServerBase Server;

    protected Connection(ILogger logger, IServerBase server, PluginExecutor pluginExecutor, IPacketReader packetReader)
    {
        _logger = logger;
        _pluginExecutor = pluginExecutor;
        _packetReader = packetReader;
        Server = server;
        CryptoSession = new AvalonCryptoSession(ServerCrypto.GetKeyPair());
        Id = Guid.NewGuid();
    }

    protected void Init(TcpClient client)
    {
        _client = client;
        RemoteEndPoint = client.Client.RemoteEndPoint?.ToString() ?? "Unknown";
        CancellationTokenSource = new CancellationTokenSource();
        Task.Factory.StartNew(SendPacketsWhenAvailable, TaskCreationOptions.LongRunning);
    }
    
    protected bool IsConnected => _client?.Connected == true;

    protected abstract void OnHandshakeFinished();

    protected abstract Task<Stream> GetStream(TcpClient client);

    protected abstract Task OnClose(bool expected = true);

    protected abstract Task OnReceive(NetworkPacket packet, Packet? payload);

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
            await foreach (var packet in _packetReader.EnumerateAsync(_stream, stoppingToken))
            {
                if (packet.Header.Type != NetworkPacketType.CMSG_MOVEMENT && packet.Header.Type != NetworkPacketType.CMSG_PONG)
                    _logger.LogDebug("IN: {Type} => {Data}", packet.Header.Type, JsonSerializer.Serialize(packet));
                await _pluginExecutor.ExecutePlugins<IPacketOperationListener>(x => x.OnPrePacketReceivedAsync(packet, Array.Empty<byte>(), stoppingToken));

                if (packet.Header.Flags.HasFlag(NetworkPacketFlags.Encrypted))
                {
                    _packetReader.Decrypt(packet, CryptoSession.Decrypt);
                }

                var payload = _packetReader.Read(packet);

                await OnReceive(packet, payload);

                await _pluginExecutor.ExecutePlugins<IPacketOperationListener>(x => x.OnPostPacketReceivedAsync(packet, Array.Empty<byte>(), stoppingToken));
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

    public void Close(bool expected = true)
    {
        if (_closed) return;
        CancellationTokenSource?.Cancel();
        _client?.Close();
        _closed = true;
        OnClose(expected);
    }

    public void Send(NetworkPacket packet)
    {
        _packetsToSend.Enqueue(packet);
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
                var packet = await _packetsToSend.DequeueAsync(CancellationTokenSource!.Token).ConfigureAwait(false);
                if (packet != null)
                {
                    try
                    {
                        if (_stream is null)
                        {
                            CancellationTokenSource?.Cancel();
                            _logger.LogCritical("Stream unexpectedly became null. This shouldn't happen");
                            break;
                        }

                        await _pluginExecutor
                            .ExecutePlugins<IPacketOperationListener>(x =>
                                x.OnPrePacketSentAsync(packet, CancellationToken.None)).ConfigureAwait(false);
                        await _stream.WriteAsync(packet).ConfigureAwait(false);
                        await _stream.FlushAsync().ConfigureAwait(false);
                        await _pluginExecutor.ExecutePlugins<IPacketOperationListener>(x =>
                                x.OnPostPacketSentAsync(packet, Array.Empty<byte>(), CancellationToken.None))
                            .ConfigureAwait(false);

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
                    if (packet.Header.Type != NetworkPacketType.SMSG_WORLD_STATE_UPDATE && packet.Header.Type != NetworkPacketType.SMSG_PING)
                        _logger.LogTrace("OUT: {Type} => {Packet}", packet.Header.Type, JsonSerializer.Serialize(packet));
                }
                else
                {
                    await Task.Delay(1).ConfigureAwait(false); // wait at least 1ms
                }
            }
            catch (SocketException)
            {
                // connection closed. Ignore
                break;
            }
        }
    }
}
