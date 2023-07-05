using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Character;

[ProtoContract]
public class CCharacterListPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_CHARACTER_LIST;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    
    [ProtoMember(1)] public int AccountId { get; set; }

    public static NetworkPacket Create(int accountId)
    {
        using var memoryStream = new MemoryStream();
        
        var authPacket = new CCharacterListPacket()
        {
            AccountId = accountId,
        };
        
        Serializer.Serialize(memoryStream, authPacket);
        
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
