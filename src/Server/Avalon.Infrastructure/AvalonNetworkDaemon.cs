using System.Net.Sockets;
using System.Text;
using Avalon.Game;
using Avalon.Game.Handlers;
using Avalon.Metrics;
using Avalon.Network;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Crypto;
using Avalon.Network.Packets.Deserialization;
using Avalon.Network.Packets.Exceptions;
using Avalon.Network.Packets.Generic;
using Avalon.Network.Packets.Movement;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.Social;
using Microsoft.Extensions.Logging;
using TcpClient = Avalon.Network.TcpClient;

namespace Avalon.Infrastructure;

public interface IAvalonNetworkDaemon : IDisposable
{
    void Start();
    void Stop();
}

public class AvalonNetworkDaemon : IAvalonNetworkDaemon
{
    private readonly ILogger<AvalonNetworkDaemon> _logger;
    private readonly CancellationTokenSource _cts;
    private readonly IAvalonTcpServer _tcpServer;
    private readonly IAvalonUdpServer _udpServer;
    private readonly IPacketDeserializer _packetDeserializer;
    private readonly IPacketSerializer _packetSerializer;
    private readonly IPacketRegistry _packetRegistry;
    private readonly IAvalonGame _game;
    private readonly IAvalonConnectionManager _connectionManager;
    private readonly IMetricsManager _metrics;

    private long _bytesReceived = 0;
    private long _udpBytesReceived = 0;
    private long _tcpBytesReceived = 0;
    
    private long _packetsReceived = 0;
    private long _udpPacketsReceived = 0;
    private long _tcpPacketsReceived = 0;

    public AvalonNetworkDaemon(
        ILogger<AvalonNetworkDaemon> logger, 
        CancellationTokenSource cts,
        IAvalonTcpServer tcpServer, 
        IAvalonUdpServer udpServer,
        IPacketDeserializer packetDeserializer,
        IPacketSerializer packetSerializer,
        IPacketRegistry packetRegistry,
        IAvalonGame game,
        IAvalonConnectionManager connectionManager,
        IMetricsManager metrics)
    {
        _logger = logger;
        _cts = cts;

        _packetDeserializer = packetDeserializer;
        _packetSerializer = packetSerializer;
        _packetRegistry = packetRegistry;
        _game = game;
        _connectionManager = connectionManager;
        _metrics = metrics;

        _udpServer = udpServer;
        _udpServer.OnPacketReceived += UdpServerOnOnPacketReceived;
        _udpServer.OnClientDisconnected += UdpServerOnClientDisconnected;
        _udpServer.OnClientTimeout += UdpServerOnClientTimeout;

        _tcpServer = tcpServer;
        _tcpServer.ClientConnected += TcpServerOnClientConnected;
    }

    public void Start()
    {
        _packetSerializer.RegisterPacketSerializers();
        _packetDeserializer.RegisterPacketDeserializers();
        
        _packetRegistry.RegisterHandler<CAuthPacket>(NetworkPacketType.CMSG_AUTH, _game.HandleAuthPacket);
        _packetRegistry.RegisterHandler<CRequestServerVersionPacket>(NetworkPacketType.CMSG_REQUEST_SERVER_VERSION, _game.HandleServerVersionPacket);
        _packetRegistry.RegisterHandler<CPingPacket>(NetworkPacketType.CMSG_PING, _game.HandlePingPacket);
        _packetRegistry.RegisterHandler<CWelcomePacket>(NetworkPacketType.CMSG_WELCOME, _connectionManager.AddConnection);
        _packetRegistry.RegisterHandler<CPongPacket>(NetworkPacketType.CMSG_PONG, _connectionManager.HandlePongPacket);
        _packetRegistry.RegisterHandler<CPlayerMovementPacket>(NetworkPacketType.CMSG_MOVEMENT, _game.HandleMovementPacket);
        _packetRegistry.RegisterHandler<CChatMessagePacket>(NetworkPacketType.CMSG_CHAT_MESSAGE, _game.HandleChatMessagePacket);
        _packetRegistry.RegisterHandler<COpenChatPacket>(NetworkPacketType.CMSG_CHAT_OPEN, _game.HandleOpenChatPacket);
        _packetRegistry.RegisterHandler<CCloseChatPacket>(NetworkPacketType.CMSG_CHAT_CLOSE, _game.HandleCloseChatPacket);

        _logger.LogInformation("Starting network daemon");
        
        _connectionManager.Start();
        
        Task.Run(_udpServer.RunAsync).ConfigureAwait(false);
        Task.Run(_tcpServer.RunAsync).ConfigureAwait(false);
        Task.Run(UpdateMetricsAsync).ConfigureAwait(false);
    }

    private async Task UpdateMetricsAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            await Task.Delay(50, _cts.Token).ConfigureAwait(false);

            _metrics.QueueMetric("network.bytes_received", Interlocked.Read(ref _bytesReceived));
            _metrics.QueueMetric("network.bytes_received.udp", Interlocked.Read(ref _udpBytesReceived));
            _metrics.QueueMetric("network.bytes_received.tcp", Interlocked.Read(ref _tcpBytesReceived));
            
            _metrics.QueueMetric("network.packets_received", Interlocked.Read(ref _packetsReceived));
            _metrics.QueueMetric("network.packets_received.udp", Interlocked.Read(ref _udpPacketsReceived));
            _metrics.QueueMetric("network.packets_received.tcp", Interlocked.Read(ref _tcpPacketsReceived));
        }
    }

    public async void Stop()
    {
        _logger.LogInformation("Stopping network daemon");
        
        _connectionManager.Stop();
        
        await _udpServer.StopAsync().ConfigureAwait(false);
        await _tcpServer.StopAsync().ConfigureAwait(false);
    }
    
    private void TcpServerOnClientConnected(object? sender, TcpClient client)
    {

        Task.Run(async () =>
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var packet = await _packetDeserializer.DeserializeFromNetwork<NetworkPacket>(client.Stream);
                    
                    try
                    {
                        if (packet == null)
                        {
                            _logger.LogWarning("Received null tcp packet from client {Endpoint}", client.RemoteAddress);
                            break;
                        }

                        Interlocked.Increment(ref _tcpPacketsReceived);
                        Interlocked.Increment(ref _packetsReceived);
                        
                        Interlocked.Add(ref _tcpBytesReceived, packet.Size);
                        Interlocked.Add(ref _bytesReceived, packet.Size);

                        var deserializedPacket = GetInnerPacket(packet);
                        
                        var handler = _packetRegistry.GetHandler(packet.Header.Type);

                        await handler.Handle(client, deserializedPacket).ConfigureAwait(false);
                    }
                    catch (PacketHandlerException e)
                    {
                        _logger.LogWarning(e, "Packet handler exception while handling packet {@Packet}", packet);
                    }
                    catch (Exception e)
                    {
                        _logger.LogWarning(e, "Exception while handling packet {@Packet}", packet);
                    }
                    
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Operation cancelled");
            }
            catch (IOException e)
            {
                if (e.InnerException is SocketException && e.InnerException.Message.Contains("An existing connection was forcibly closed by the remote host"))
                {
                    //_logger.LogInformation("Client {Endpoint} disconnected", client.Socket.RemoteEndPoint.ToString());
                    //_movementManager.RemovePlayer(client.Socket.RemoteEndPoint);
                }
                else
                {
                    _logger.LogWarning(e, "IOException while reading from client stream");
                }
            }

            await client.Stream.DisposeAsync();

        }).ConfigureAwait(false);
    }
    
    private void UdpServerOnOnPacketReceived(object? sender, UdpClientPacket clientPacket)
    {
        Task.Run(async () =>
        {
            await using var stream = new MemoryStream(clientPacket.Buffer);
        
            var packet = await _packetDeserializer.DeserializeFromNetwork<NetworkPacket>(stream);

            try
            {
                if (packet == null)
                {
                    _logger.LogWarning("Received null udp packet from client {Endpoint}", clientPacket.RemoteAddress);
                    return;
                }
                
                Interlocked.Increment(ref _udpPacketsReceived);
                Interlocked.Increment(ref _packetsReceived);
                        
                Interlocked.Add(ref _udpBytesReceived, packet.Size);
                Interlocked.Add(ref _bytesReceived, packet.Size);
                
                var handler = _packetRegistry.GetHandler(packet.Header.Type);
                
                var deserializedPacket = GetInnerPacket(packet);
                
                await handler.Handle(clientPacket, deserializedPacket).ConfigureAwait(false);
            }
            catch (PacketHandlerException e)
            {
                _logger.LogWarning(e, "Packet handler exception while handling packet {@Packet}", packet);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Exception while handling packet {@Packet}", packet);
            }

        }).ConfigureAwait(false);
    }
    
    private void UdpServerOnClientTimeout(object? sender, UdpClientPacket clientPacket)
    {
        _logger.LogInformation("UdpServerOnClientTimeout {Endpoint} timed out", clientPacket.RemoteAddress);
        //_connectionManager.RemoveConnection(clientPacket.RemoteAddress);
    }

    private void UdpServerOnClientDisconnected(object? sender, UdpClientPacket clientPacket)
    {
        _logger.LogInformation("UdpServerOnClientDisconnected {Endpoint} disconnected", clientPacket.RemoteAddress);
        _connectionManager.RemoveConnection(clientPacket.RemoteAddress);
    }
    
    private Packet GetInnerPacket(NetworkPacket packet)
    {
        return packet.Header.Type switch
        {
            NetworkPacketType.CMSG_AUTH => _packetDeserializer.Deserialize<CAuthPacket>(packet.Header.Type, packet.Payload),
            NetworkPacketType.CMSG_WELCOME => _packetDeserializer.Deserialize<CWelcomePacket>(packet.Header.Type, packet.Payload),
            NetworkPacketType.CMSG_MOVEMENT => _packetDeserializer.Deserialize<CPlayerMovementPacket>(packet.Header.Type, packet.Payload),
            NetworkPacketType.CMSG_REQUEST_SERVER_VERSION => _packetDeserializer.Deserialize<CRequestServerVersionPacket>(packet.Header.Type, packet.Payload),
            NetworkPacketType.CMSG_REQUEST_ENCRYPTION_KEY => _packetDeserializer.Deserialize<CRequestCryptoKeyPacket>(packet.Header.Type, packet.Payload),
            NetworkPacketType.CMSG_PING => _packetDeserializer.Deserialize<CPingPacket>(packet.Header.Type, packet.Payload),
            NetworkPacketType.CMSG_PONG => _packetDeserializer.Deserialize<CPongPacket>(packet.Header.Type, packet.Payload),
            NetworkPacketType.CMSG_CHAT_MESSAGE => _packetDeserializer.Deserialize<CChatMessagePacket>(packet.Header.Type, packet.Payload),
            NetworkPacketType.CMSG_CHAT_OPEN => _packetDeserializer.Deserialize<COpenChatPacket>(packet.Header.Type, packet.Payload),
            NetworkPacketType.CMSG_CHAT_CLOSE => _packetDeserializer.Deserialize<CCloseChatPacket>(packet.Header.Type, packet.Payload),
            _ => throw new PacketHandlerException("Unknown packet type " + packet.Header.Type)
        };
    }

    public void Dispose()
    {
        _connectionManager.Dispose();
        _tcpServer.Dispose();
        _udpServer.Dispose();
        
        GC.SuppressFinalize(this);
    }
}
