using System.Collections.Concurrent;
using System.Net;
using Avalon.Network.Abstractions;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Deserialization;
using Avalon.Network.Packets.Movement;
using Avalon.Network.Packets.Serialization;
using Microsoft.Extensions.Logging;

namespace Avalon.Game.Handlers;

public interface IAvalonMovementManager
{
    Task HandleJumpPacket(IRemoteSource source, NetworkPacket packet);
    Task HandleMovementPacket(IRemoteSource source, NetworkPacket packet);
    Task HandleWelcomePacket(IRemoteSource source, NetworkPacket packet);
    void RemovePlayer(EndPoint endpoint);
}

class Character
{
    public float X { get; set; }
    public float Y { get; set; }
    public float VelocityX { get; set; }
    public float VelocityY { get; set; }
    public float ElapsedGameTime { get; set; }
}

class Client
{
    public Guid ClientId { get; set; }
    public UdpClientPacket? UdpConnection { get; set; }
    public TcpClient? TcpConnection { get; set; }
    public Character Character { get; set; }
}

public class AvalonMovementManager : IAvalonMovementManager
{
    private readonly ILogger<AvalonMovementManager> _logger;
    private readonly CancellationTokenSource _cts;
    private readonly IPacketDeserializer _packetDeserializer;
    private readonly IPacketSerializer _packetSerializer;
    private readonly ConcurrentDictionary<Guid, Client> _clients;

    public AvalonMovementManager(ILogger<AvalonMovementManager> logger, 
                                 CancellationTokenSource cts, 
                                 IPacketDeserializer packetDeserializer,
                                 IPacketSerializer packetSerializer)
    {
        _logger = logger;
        _cts = cts;
        _packetDeserializer = packetDeserializer;
        _packetSerializer = packetSerializer;
        _clients = new ConcurrentDictionary<Guid, Client>();

        Task.Run(BroadcastPositions);
    }
    
    public async Task HandleJumpPacket(IRemoteSource source, NetworkPacket packet)
    {
        var client = source.AsUdpClient();
        
        _logger.LogDebug("Handling jump packet from {EndPoint}", client.EndPoint);

        var bytesSent = await client.SendResponseAsync(client.Buffer);
        
        _logger.LogDebug("Sent {BytesSent} bytes to {EndPoint}", bytesSent, client.EndPoint);
    }

    public async Task HandleMovementPacket(IRemoteSource source, NetworkPacket packet)
    {
        //var client = source.AsTcpClient();
        
        //_logger.LogDebug("Handling movement packet from {EndPoint}", client.Socket.RemoteEndPoint);

        var movementPacket = _packetDeserializer.Deserialize<CPlayerMovementPacket>(
            packet.Header.Type, 
            packet.Payload
        );

        if (_clients.TryGetValue(movementPacket.ClientId, out var client))
        {
            client.Character.X = movementPacket.X;
            client.Character.Y = movementPacket.Y;
            client.Character.VelocityX = movementPacket.VelocityX;
            client.Character.VelocityY = movementPacket.VelocityY;
            client.Character.ElapsedGameTime = movementPacket.ElapsedGameTime;
        }
        
        //_logger.LogDebug("Movement packet: {@MovementPacket}", movementPacket);
    }

    public async Task HandleWelcomePacket(IRemoteSource source, NetworkPacket packet)
    {
        var welcomePacket = _packetDeserializer.Deserialize<CWelcomePacket>(
            packet.Header.Type, 
            packet.Payload
        );
        
        if (source is UdpClientPacket udpClient)
        {
            _logger.LogDebug("Handling welcome UDP packet from {EndPoint}", udpClient.EndPoint);
            
            _clients.AddOrUpdate(welcomePacket.ClientId, guid => new Client
            {
                Character = new Character(),
                ClientId = welcomePacket.ClientId,
                TcpConnection = null,
                UdpConnection = udpClient
            }, (guid, existingClient) =>
            {
                existingClient.UdpConnection = udpClient;
                return existingClient;
            });
        }
        else if (source is TcpClient tcpClient)
        {
            _logger.LogDebug("Handling welcome TCP packet from {EndPoint}", tcpClient.Socket.RemoteEndPoint);
            
            _clients.AddOrUpdate(welcomePacket.ClientId, guid => new Client
            {
                Character = new Character(),
                ClientId = welcomePacket.ClientId,
                TcpConnection = tcpClient,
                UdpConnection = null
            }, (guid, existingClient) =>
            {
                existingClient.TcpConnection = tcpClient;
                return existingClient;
            });

            foreach (var (id, client) in _clients)
            {
                if (id == welcomePacket.ClientId)
                {
                    continue;
                }
                await Send(welcomePacket.ClientId, SPlayerConnectedPacket.Create(id));
            }
            
            await BroadcastToOthers(true, welcomePacket.ClientId, SPlayerConnectedPacket.Create(welcomePacket.ClientId));
        }
    }

    public void RemovePlayer(EndPoint endpoint)
    {
        
        foreach (var (disconnectedId, client) in _clients)
        {
            if (client.TcpConnection?.Socket.RemoteEndPoint != endpoint) continue;
            
            _logger.LogDebug("Removing player because he disconnected {ClientId}", disconnectedId);

            var packet = SPlayerDisconnectedPacket.Create(disconnectedId);

            foreach (var (otherId, otherClient) in _clients)
            {
                if (otherId == disconnectedId || otherClient.TcpConnection == null)
                {
                    continue;
                }

                _packetSerializer.SerializeToNetwork(otherClient.TcpConnection.Stream, packet);
            }
                
            _clients.TryRemove(disconnectedId, out _);

            break;
        }
    }
    
    private async Task Send(Guid clientId, NetworkPacket packet)
    {
        if (_clients.TryGetValue(clientId, out var client))
        {
            if (client.TcpConnection != null)
            {
                try
                {
                    await _packetSerializer.SerializeToNetwork(client.TcpConnection.Stream, packet);
                }
                catch (Exception e)
                {
                    //ignored
                }
            }
        }
    }

    private async Task BroadcastAll(NetworkPacket packet)
    {
        foreach (var (id, client) in _clients)
        {
            if (client.TcpConnection == null)
            {
                continue;
            }
            await _packetSerializer.SerializeToNetwork(client.TcpConnection.Stream, packet);
        }
    }
    
    private async Task BroadcastToOthers(bool tcp, Guid except, NetworkPacket packet)
    {
        foreach (var (id, client) in _clients)
        {
            if (id == except)
            {
                continue;
            }
            
            if (client.TcpConnection == null)
            {
                continue;
            }
 
            try
            {
                if (tcp)
                {
                    if (client.TcpConnection == null)
                    {
                        continue;
                    }
                    
                    await _packetSerializer.SerializeToNetwork(client.TcpConnection.Stream, packet);
                }
                else
                {
                    if (client.UdpConnection == null)
                    {
                        continue;
                    }
                
                    var ms = new MemoryStream();
                    await _packetSerializer.SerializeToNetwork(ms, packet);
                    await client.UdpConnection.SendResponseAsync(ms.ToArray());
                }
            }
            catch (Exception)
            {
                // ignored
            }
        }
    }

    private async Task BroadcastPositions()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                await Task.Delay(50, _cts.Token);

                foreach (var (id, client) in _clients)
                {
                    var packet = SPlayerPositionUpdatePacket.Create(
                        id, 
                        client.Character.X, 
                        client.Character.Y,
                        client.Character.VelocityX,
                        client.Character.VelocityY,
                        client.Character.ElapsedGameTime
                    );
                    
                    await BroadcastToOthers(false, id, packet);
                }
            }
        }
        catch (OperationCanceledException e)
        {
            _logger.LogWarning(e, "Broadcasting positions cancelled", e);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error while broadcasting positions: {Exception}", e);
        }
    }
}
