using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Character;

[ProtoContract]
public class SCharacterSelectedPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_CHARACTER_SELECTED;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    
    [ProtoMember(1)] public int AccountId { get; set; }
    [ProtoMember(2)] public CharacterInfo Character { get; set; }

    public static NetworkPacket Create(int accountId, CharacterInfo character)
    {
        using var memoryStream = new MemoryStream();
        
        var authPacket = new SCharacterSelectedPacket()
        {
            AccountId = accountId,
            Character = character
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
