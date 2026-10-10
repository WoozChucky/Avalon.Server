using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Quest;

/// <summary>
/// Server→Client (#433): the quest a dialogue option names, in full, after the player chose it. The conversation
/// stays on the NPC's root; declining is the client closing this window, and needs no packet.
/// </summary>
[ProtoContract]
public class SQuestOfferPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_QUEST_OFFER;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public uint QuestId { get; set; }
    [ProtoMember(2)] public ulong NpcGuid { get; set; }
    [ProtoMember(3)] public QuestOfferMode Mode { get; set; }
    [ProtoMember(4)] public QuestDisplayDto? Quest { get; set; }

    public static OutboundPacket Create(uint questId, ulong npcGuid, QuestOfferMode mode, QuestDisplayDto quest, PacketEncoder encoder)
    {
        SQuestOfferPacket message = PacketEncoder.Scratch<SQuestOfferPacket>();
        message.QuestId = questId;
        message.NpcGuid = npcGuid;
        message.Mode = mode;
        message.Quest = quest;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }
}
