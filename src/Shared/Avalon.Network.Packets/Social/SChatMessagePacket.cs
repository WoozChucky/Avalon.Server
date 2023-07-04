using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Social;

[ProtoContract]
public class SChatMessagePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_CHAT_MESSAGE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    
    [ProtoMember(1)] public int AccountId { get; set; }
    [ProtoMember(2)] public int CharacterId { get; set; }
    [ProtoMember(3)] public string CharacterName { get; set; }
    [ProtoMember(4)] public string Message { get; set; }
    [ProtoMember(5)] public DateTime DateTime { get; set; }

    public static NetworkPacket Create(int accountId, int characterId, string characterName, string message, DateTime dateTime)
    {
        using var memoryStream = new MemoryStream();
        
        var movementPacket = new SChatMessagePacket()
        {
            AccountId = accountId,
            CharacterId = characterId,
            CharacterName = characterName,
            Message = message,
            DateTime = dateTime
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
