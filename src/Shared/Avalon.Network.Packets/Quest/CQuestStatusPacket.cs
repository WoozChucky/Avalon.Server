using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Map;
using ProtoBuf;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Network.Packets.Quest;

[ProtoContract]
public class CQuestStatusPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_QUEST_STATUS;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public int AccountId { get; set; }
    [ProtoMember(2)] public int CharacterId { get; set; }
    [ProtoMember(3)] public int QuestId { get; set; }

    public static NetworkPacket Create(int accountId, int characterId, int questId, EncryptFunc encryptFunc)
    {
        using var memoryStream = new MemoryStream();

        var authPacket = new CQuestStatusPacket()
        {
            AccountId = accountId,
            CharacterId = characterId,
            QuestId = questId
        };

        Serializer.Serialize(memoryStream, authPacket);

        return new NetworkPacket
        {
            Header = new NetworkPacketHeader
            {
                Type = PacketType,
                Flags = Flags,
                Protocol = Protocol,
                Version = 0
            },
            Payload = memoryStream.ToArray()
        };
    }
}
