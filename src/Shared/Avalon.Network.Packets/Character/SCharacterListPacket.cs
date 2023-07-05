using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Character;

[ProtoContract]
public class SCharacterListPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_CHARACTER_LIST;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    
    [ProtoMember(1)] public int AccountId { get; set; }
    [ProtoMember(2)] public int CharacterCount { get; set; }
    [ProtoMember(3)] public int MaxCharacterCount { get; set; }
    [ProtoMember(4)] public CharacterInfo[] Characters { get; set; }

    public static NetworkPacket Create(int accountId, int characterCount, int maxCharacterCount, CharacterInfo[] characters)
    {
        using var memoryStream = new MemoryStream();
        
        var authPacket = new SCharacterListPacket()
        {
            AccountId = accountId,
            CharacterCount = characterCount,
            MaxCharacterCount = maxCharacterCount,
            Characters = characters
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

[ProtoContract]
public class CharacterInfo
{
    [ProtoMember(1)] public int CharacterId { get; set; }
    [ProtoMember(2)] public string Name { get; set; }
    [ProtoMember(3)] public int Level { get; set; }
    [ProtoMember(4)] public int Class { get; set; }
    [ProtoMember(5)] public float X { get; set; }
    [ProtoMember(6)] public float Y { get; set; }
}
