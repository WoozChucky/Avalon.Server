using System.Net.Sockets;
using System.Text;
using Avalon.Game;
using Avalon.Game.Handlers;
using Avalon.Network;
using Avalon.Network.Abstractions;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Crypto;
using Avalon.Network.Packets.Deserialization;
using Avalon.Network.Packets.Exceptions;
using Avalon.Network.Packets.Generic;
using Avalon.Network.Packets.Movement;
using Avalon.Network.Packets.Serialization;
using Microsoft.Extensions.Logging;
using TcpClient = Avalon.Network.Abstractions.TcpClient;

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
    private readonly IAvalonMovementManager _movementManager;

    public AvalonNetworkDaemon(
        ILogger<AvalonNetworkDaemon> logger, 
        CancellationTokenSource cts,
        IAvalonTcpServer tcpServer, 
        IAvalonUdpServer udpServer,
        IPacketDeserializer packetDeserializer,
        IPacketSerializer packetSerializer,
        IPacketRegistry packetRegistry,
        IAvalonGame game,
        IAvalonMovementManager movementManager)
    {
        _logger = logger;
        _cts = cts;

        _packetDeserializer = packetDeserializer;
        _packetSerializer = packetSerializer;
        _packetRegistry = packetRegistry;
        _game = game;
        _movementManager = movementManager;

        _udpServer = udpServer;
        _udpServer.OnPacketReceived += UdpServerOnOnPacketReceived;
        
        _tcpServer = tcpServer;
        _tcpServer.ClientConnected += TcpServerOnClientConnected;
    }
    
    public void Start()
    {
        _packetSerializer.RegisterPacketSerializers();
        _packetDeserializer.RegisterPacketDeserializers();
        
        _packetRegistry.RegisterHandler<CRequestServerVersionPacket>(NetworkPacketType.CMSG_REQUEST_SERVER_VERSION, _game.HandleServerVersionPacket);
        _packetRegistry.RegisterHandler<CPingPacket>(NetworkPacketType.CMSG_PING, _game.HandlePingPacket);
        _packetRegistry.RegisterHandler<CWelcomePacket>(NetworkPacketType.CMSG_WELCOME, _movementManager.HandleWelcomePacket);
        _packetRegistry.RegisterHandler<CPlayerMovementPacket>(NetworkPacketType.CMSG_MOVEMENT, _movementManager.HandleMovementPacket);

        _logger.LogInformation("Starting network daemon");
        
        Task.Run(_udpServer.RunAsync).ConfigureAwait(false);
        Task.Run(_tcpServer.RunAsync).ConfigureAwait(false);
    }

    public async void Stop()
    {
        _logger.LogInformation("Stopping network daemon");
        await _udpServer.StopAsync().ConfigureAwait(false);
        await _tcpServer.StopAsync().ConfigureAwait(false);
    }
    
    /*
    private async Task Handler(IRemoteSource source, NetworkPacket packet)
    {
        // Receive a response from the server
        using var messageStream = new MemoryStream();
        
        var client = (TcpClient) source;
        
        var encryptionKeyPacket = _packetDeserializer.Deserialize<CRequestCryptoKeyPacket>(
            NetworkPacketType.CMSG_REQUEST_ENCRYPTION_KEY, 
            packet.Payload
        );
        
        if (encryptionKeyPacket != null)
        {
            var sCryptoKeyPacket = new SCryptoKeyPacket
            {
                Key = "123456789"u8.ToArray()
            };

            await _packetSerializer.Serialize(messageStream, sCryptoKeyPacket);
                                
            var resultingPacket = new NetworkPacket
            {
                Header = new NetworkPacketHeader
                {
                    Type = NetworkPacketType.SMSG_ENCRYPTION_KEY,
                    Flags = NetworkPacketFlags.None,
                    Version = 0
                },
                Payload = messageStream.ToArray()
            };
            await _packetSerializer.SerializeToNetwork(client.Stream, resultingPacket);
        }
    }
    */
    
    private void TcpServerOnClientConnected(object? sender, TcpClient client)
    {
        _logger.LogInformation("Client connected from {EndPoint}", client.Socket.RemoteEndPoint.ToString());
        
        Task.Run(async () =>
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var packet = await _packetDeserializer.DeserializeFromNetwork<NetworkPacket>(client.Stream);
                    
                    try
                    {
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
                    _logger.LogInformation("Client {Endpoint} disconnected", client.Socket.RemoteEndPoint.ToString());
                    _movementManager.RemovePlayer(client.Socket.RemoteEndPoint);
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
    
    private Packet GetInnerPacket(NetworkPacket packet)
    {
        return packet.Header.Type switch
        {
            NetworkPacketType.CMSG_WELCOME => _packetDeserializer.Deserialize<CWelcomePacket>(packet.Header.Type, packet.Payload),
            NetworkPacketType.CMSG_MOVEMENT => _packetDeserializer.Deserialize<CPlayerMovementPacket>(packet.Header.Type, packet.Payload),
            NetworkPacketType.CMSG_REQUEST_SERVER_VERSION => _packetDeserializer.Deserialize<CRequestServerVersionPacket>(packet.Header.Type, packet.Payload),
            NetworkPacketType.CMSG_REQUEST_ENCRYPTION_KEY => _packetDeserializer.Deserialize<CRequestCryptoKeyPacket>(packet.Header.Type, packet.Payload),
            NetworkPacketType.CMSG_PING => _packetDeserializer.Deserialize<CPingPacket>(packet.Header.Type, packet.Payload),
            _ => throw new PacketHandlerException("Unknown packet type " + packet.Header.Type)
        };
    }

    public void Dispose()
    {
        _tcpServer.Dispose();
        _udpServer.Dispose();
        
        GC.SuppressFinalize(this);
    }
}
