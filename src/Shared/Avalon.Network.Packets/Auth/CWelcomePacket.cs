using ProtoBuf;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
public class CWelcomePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_WELCOME;
    public static NetworkProtocol Protocol = NetworkProtocol.Both;
    
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
                Protocol = Protocol,
                Version = 0
            },
            Payload = memoryStream.ToArray()
        };
    }
}
