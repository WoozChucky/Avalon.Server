using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Character;

[ProtoContract]
public class CCharacterDeletePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_CHARACTER_DELETE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    
    [ProtoMember(1)] public int AccountId { get; set; }
    [ProtoMember(2)] public int CharacterId { get; set; }

    public static NetworkPacket Create(int accountId, int characterId)
    {
        using var memoryStream = new MemoryStream();
        
        var authPacket = new CCharacterDeletePacket()
        {
            AccountId = accountId,
            CharacterId = characterId
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
