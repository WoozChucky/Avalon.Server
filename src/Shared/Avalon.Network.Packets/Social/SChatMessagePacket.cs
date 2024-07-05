using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Social;

[ProtoContract]
public class SChatMessagePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_CHAT_MESSAGE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;
    
    [ProtoMember(1)] public ulong AccountId { get; set; }
    [ProtoMember(2)] public ulong CharacterId { get; set; }
    [ProtoMember(3)] public string CharacterName { get; set; }
    [ProtoMember(4)] public string Message { get; set; }
    [ProtoMember(5)] public DateTime DateTime { get; set; }

    public static NetworkPacket Create(ulong accountId, ulong characterId, string characterName, string message, DateTime dateTime, Func<byte[], byte[]> encryptFunc)
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
        
        var buffer = encryptFunc(memoryStream.ToArray());
        
        return new NetworkPacket
        {
            Header = new NetworkPacketHeader
            {
                Type = PacketType,
                Flags = Flags,
                Protocol = Protocol,
                Version = 0
            },
            Payload = buffer
        };
    }
}
