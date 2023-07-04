using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Social;

[ProtoContract]
public class COpenChatPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_CHAT_OPEN;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    
    [ProtoMember(1)] public int AccountId { get; set; }
    [ProtoMember(2)] public int CharacterId { get; set; }

    public static NetworkPacket Create(int accountId, int characterId)
    {
        using var memoryStream = new MemoryStream();
        
        var movementPacket = new COpenChatPacket()
        {
            AccountId = accountId,
            CharacterId = characterId
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
