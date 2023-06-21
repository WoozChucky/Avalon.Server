using Avalon.Network;
using Avalon.Network.Abstractions;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Deserialization;
using Avalon.Network.Packets.Movement;
using Microsoft.Extensions.Logging;

namespace Avalon.Game.Handlers;

public interface IAvalonMovementManager
{
    Task HandleJumpPacket(IRemoteSource source, NetworkPacket packet);
    Task HandleMovementPacket(IRemoteSource source, NetworkPacket packet);
}

public class AvalonMovementManager : IAvalonMovementManager
{
    private readonly ILogger<AvalonMovementManager> _logger;
    private readonly IPacketDeserializer _packetDeserializer;

    public AvalonMovementManager(ILogger<AvalonMovementManager> logger, IPacketDeserializer packetDeserializer)
    {
        _logger = logger;
        _packetDeserializer = packetDeserializer;
    }
    
    public async Task HandleJumpPacket(IRemoteSource source, NetworkPacket packet)
    {
        var client = (UdpClientPacket) source;
        
        _logger.LogDebug("Handling jump packet from {EndPoint}", client.EndPoint);

        var bytesSent = await client.SendResponseAsync(client.Buffer);
        
        _logger.LogDebug("Sent {BytesSent} bytes to {EndPoint}", bytesSent, client.EndPoint);
    }

    public async Task HandleMovementPacket(IRemoteSource source, NetworkPacket packet)
    {
        var client = (TcpClient)source;
        
        _logger.LogDebug("Handling movement packet from {EndPoint}", client.Socket.RemoteEndPoint);

        var movementPacket = _packetDeserializer.Deserialize<CPlayerMovementPacket>(
            NetworkPacketType.CMSG_REQUEST_ENCRYPTION_KEY, 
            packet.Payload
        );
        
        _logger.LogDebug("Movement packet: {@MovementPacket}", movementPacket);
    }
}
