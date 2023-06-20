using System.Net.Sockets;
using System.Text;
using Avalon.Game;
using Avalon.Game.Handlers;
using Avalon.Metrics;
using Avalon.Network;
using Avalon.Network.Abstractions;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Crypto;
using Avalon.Network.Packets.Deserialization;
using Avalon.Network.Packets.Exceptions;
using Avalon.Network.Packets.Serialization;
using Microsoft.Extensions.Logging;
using TcpClient = Avalon.Network.Abstractions.TcpClient;

namespace Avalon.Infrastructure;

public interface IAvalonInfrastructure : IDisposable
{
    void Start();
    void Stop();
    void Loop(int waitTimeMs);
}

public class AvalonInfrastructure : IAvalonInfrastructure
{
    private readonly CancellationTokenSource _cts;
    private readonly ILogger<AvalonInfrastructure> _logger;
    private readonly IAvalonUdpServer _udpServer;
    private readonly IPacketDeserializer _packetDeserializer;
    private readonly IPacketSerializer _packetSerializer;
    private readonly IPacketHandlerRegistry _packetHandlerRegistry;
    private readonly IAvalonTcpServer _tcpServer;
    private readonly IAvalonMovementManager _movementManager;
    private readonly IMetricsManager _metricsManager;

    public AvalonInfrastructure(
        CancellationTokenSource cts,
        ILogger<AvalonInfrastructure> logger,
        IAvalonTcpServer tcpServer, 
        IAvalonUdpServer udpServer,
        IPacketDeserializer packetDeserializer,
        IPacketSerializer packetSerializer,
        IPacketHandlerRegistry packetHandlerRegistry,
        IAvalonMovementManager movementManager,
        IMetricsManager metricsManager
        )
    {
        _cts = cts;
        _logger = logger;

        _packetDeserializer = packetDeserializer;
        _packetSerializer = packetSerializer;
        _packetHandlerRegistry = packetHandlerRegistry;
        _movementManager = movementManager;
        _metricsManager = metricsManager;

        _udpServer = udpServer;
        _udpServer.OnPacketReceived += UdpServerOnOnPacketReceived;
        
        _tcpServer = tcpServer;
        _tcpServer.ClientConnected += TcpServerOnClientConnected;
    }

    public void Start()
    {
        _packetSerializer.RegisterPacketSerializers();
        _packetDeserializer.RegisterPacketDeserializers();

        _metricsManager.QueueEvent("AvalonInfrastructureStatus", "Online");
        
        _packetHandlerRegistry.RegisterHandler(NetworkPacketType.CMSG_JUMP, _movementManager.HandleJumpPacket);
        _packetHandlerRegistry.RegisterHandler(NetworkPacketType.CMSG_REQUEST_ENCRYPTION_KEY, Handler);
        

        Task.Run(_udpServer.RunAsync).ConfigureAwait(false);
        Task.Run(_tcpServer.RunAsync).ConfigureAwait(false);
    }

    public async void Stop()
    {
        _metricsManager.Stop();
        await _udpServer.StopAsync().ConfigureAwait(false);
        await _tcpServer.StopAsync().ConfigureAwait(false);
        _cts.Cancel();
    }

    public void Loop(int waitTimeMs)
    {
        try
        {

            Thread.Sleep(waitTimeMs);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "");
            throw;
        }
    }

    public void Dispose()
    {
        _logger.LogInformation("Disposing AvalonInfrastructure...");
        _udpServer.Dispose();
        _tcpServer.Dispose();
        _cts.Dispose();
        _metricsManager.QueueEvent("AvalonInfrastructureStatus", "Offline");
        GC.SuppressFinalize(this);
    }
    
    private async Task Handler(IRemoteSource source, NetworkPacket packet)
    {
        // Receive a response from the server
        using var messageStream = new MemoryStream();
        
        var client = (TcpClient) source;
        
        var encryptionKeyPacket = _packetDeserializer.Deserialize<CRequestCryptoKeyPacket>(
            NetworkPacketType.CMSG_REQUEST_ENCRYPTION_KEY, 
            packet.Payload
        );
        //textBox1.Text = Encoding.UTF8.GetString(encryptionKeyPacket.Key);
        if (encryptionKeyPacket != null)
        {
            var sCryptoKeyPacket = new SCryptoKeyPacket
            {
                Key = Encoding.UTF8.GetBytes("123456789")
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

    private async void TcpServerOnClientConnected(object? sender, TcpClient client)
    {
        _logger.LogInformation("Client connected from {@EndPoint}", client.Socket.RemoteEndPoint);
        
#pragma warning disable CS4014
        Task.Run(async () =>
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    
                    var packet = await _packetDeserializer.DeserializeFromNetwork<NetworkPacket>(client.Stream);
                    
                    try
                    {
                        var handler = _packetHandlerRegistry.GetHandler(packet.Header.Type);
                        
                        await handler(client, packet).ConfigureAwait(false);
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
                    _logger.LogInformation("Client {@Endpoint} disconnected", client.Socket.RemoteEndPoint);
                }
                else
                {
                    _logger.LogWarning(e, "IOException while reading from client stream");
                }
            }

            await client.Stream.DisposeAsync();

        }).ConfigureAwait(false);
#pragma warning restore CS4014
    }
    
    private async void UdpServerOnOnPacketReceived(object? sender, UdpClientPacket clientPacket)
    {
#pragma warning disable CS4014
        Task.Run(async () =>
        {
            await using var stream = new MemoryStream(clientPacket.Buffer);
        
            var packet = await _packetDeserializer.DeserializeFromNetwork<NetworkPacket>(stream);

            try
            {
                var handler = _packetHandlerRegistry.GetHandler(packet.Header.Type);
        
                await handler(clientPacket, packet).ConfigureAwait(false);
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
#pragma warning restore CS4014
    }
}
