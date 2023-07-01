using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Social;

[ProtoContract]
public class SOpenChatPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_CHAT_OPEN;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    
    [ProtoMember(1)] public string ClientId { get; set; }

    public static NetworkPacket Create(string clientId)
    {
        using var memoryStream = new MemoryStream();
        
        var movementPacket = new SOpenChatPacket()
        {
            ClientId = clientId
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
