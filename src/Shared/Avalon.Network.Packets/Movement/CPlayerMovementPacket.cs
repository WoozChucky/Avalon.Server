using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Movement;

[ProtoContract]
public class CPlayerMovementPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_MOVEMENT;
    public static NetworkProtocol Protocol = NetworkProtocol.Udp;
    
    [ProtoMember(1)] public int AccountId { get; set; }
    [ProtoMember(2)] public int CharacterId { get; set; }
    [ProtoMember(3)] public float ElapsedGameTime { get; set; }
    [ProtoMember(4)] public float X { get; set; }
    [ProtoMember(5)] public float Y { get; set; }
    [ProtoMember(6)] public float VelocityX { get; set; }
    [ProtoMember(7)] public float VelocityY { get; set; }

    public static NetworkPacket Create(int accountId, int characterId, float time, float x, float y, float velX, float velY)
    {
        using var memoryStream = new MemoryStream();
        
        var movementPacket = new CPlayerMovementPacket()
        {
            AccountId = accountId,
            CharacterId = characterId,
            ElapsedGameTime = time,
            X = x,
            Y = y,
            VelocityX = velX,
            VelocityY = velY
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
