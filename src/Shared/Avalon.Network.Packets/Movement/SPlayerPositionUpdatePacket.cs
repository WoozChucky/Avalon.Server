using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Movement;

[ProtoContract]
public class SPlayerPositionUpdatePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_PLAYER_POSITION_UPDATE;
    public static NetworkProtocol Protocol = NetworkProtocol.Udp;
    private const NetworkChannel Channel = NetworkChannel.PlayerMovement;
    
    [ProtoMember(1)] public string ClientId { get; set; }
    [ProtoMember(2)] public float PositionX { get; set; }
    [ProtoMember(3)] public float PositionY { get; set; }
    [ProtoMember(4)] public float VelocityX { get; set; }
    [ProtoMember(5)] public float VelocityY { get; set; }
    [ProtoMember(6)] public bool Chatting { get; set; }
    [ProtoMember(7)] public float Elapsed { get; set; }
    
    public static NetworkPacket Create(string clientId, float x, float y, float velX, float velY, bool chatting, float elapsed)
    {
        using var memoryStream = new MemoryStream();
        
        var movementPacket = new SPlayerPositionUpdatePacket()
        {
            ClientId = clientId,
            PositionX = x,
            PositionY = y,
            VelocityX = velX,
            VelocityY = velY,
            Chatting = chatting,
            Elapsed = elapsed
        };
        
        Serializer.Serialize(memoryStream, movementPacket);
        
        return new NetworkPacket
        {
            Header = new NetworkPacketHeader
            {
                Type = PacketType,
                Flags = NetworkPacketFlags.None,
                Protocol = Protocol,
                Channel = Channel,
                Version = 0
            },
            Payload = memoryStream.ToArray()
        };
    }
}
