using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Movement;

[ProtoContract]
public class SPlayerPositionUpdatePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_PLAYER_POSITION_UPDATE;
    public static NetworkProtocol Protocol = NetworkProtocol.Udp;
    
    [ProtoMember(1)] public int AccountId { get; set; }
    [ProtoMember(2)] public int CharacterId { get; set; }
    [ProtoMember(3)] public float PositionX { get; set; }
    [ProtoMember(4)] public float PositionY { get; set; }
    [ProtoMember(5)] public float VelocityX { get; set; }
    [ProtoMember(6)] public float VelocityY { get; set; }
    [ProtoMember(7)] public bool Chatting { get; set; }
    [ProtoMember(8)] public float Elapsed { get; set; }
    
    public static NetworkPacket Create(int accountId, int characterId, float x, float y, float velX, float velY, bool chatting, float elapsed)
    {
        using var memoryStream = new MemoryStream();
        
        var movementPacket = new SPlayerPositionUpdatePacket()
        {
            AccountId = accountId,
            CharacterId = characterId,
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
                Version = 0
            },
            Payload = memoryStream.ToArray()
        };
    }
}
