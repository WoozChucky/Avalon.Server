using ProtoBuf;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
public class CWelcomePacket
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_WELCOME;
    [ProtoMember(1)] public Guid ClientId { get; set; }
    
    public static NetworkPacket Create(Guid clientId)
    {
        using var memoryStream = new MemoryStream();
        
        var welcomePacket = new CWelcomePacket()
        {
            ClientId = clientId
        };
        
        Serializer.Serialize(memoryStream, welcomePacket);
        
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
