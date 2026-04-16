using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Map;
using ProtoBuf;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Network.Packets.Quest;

[ProtoContract]
public class CQuestListPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_QUEST_LIST;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;

    [ProtoMember(1)] public int AccountId { get; set; }
    [ProtoMember(2)] public int CharacterId { get; set; }
    [ProtoMember(3)] public int MapId { get; set; }

    public static NetworkPacket Create(int accountId, int characterId, int mapId, EncryptFunc encryptFunc)
    {
        using var memoryStream = new MemoryStream();

        var authPacket = new CQuestListPacket()
        {
            AccountId = accountId,
            CharacterId = characterId,
            MapId = mapId
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
