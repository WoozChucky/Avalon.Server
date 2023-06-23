using ProtoBuf;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
public class SPlayerDisconnectedPacket
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_PLAYER_DISCONNECTED;
    [ProtoMember(1)] public Guid ClientId { get; set; }
    
    public static NetworkPacket Create(Guid clientId)
    {
        using var memoryStream = new MemoryStream();
        
        var byePacket = new SPlayerDisconnectedPacket()
        {
            ClientId = clientId
        };
        
        Serializer.Serialize(memoryStream, byePacket);
        
        return new NetworkPacket
        {
            Header = new NetworkPacketHeader
            {
                Type = PacketType,
                Flags = NetworkPacketFlags.None,
                Version = 0
            },
            Payload = memoryStream.ToArray()
        };
    }
}
