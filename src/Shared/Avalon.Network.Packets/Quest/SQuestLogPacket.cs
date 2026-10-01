using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Quest;

/// <summary>
/// The whole quest log (#433), once, on the tick the selected character enters the world: every held quest with its
/// display data and progress, and the ids turned in.
/// </summary>
[ProtoContract]
public class SQuestLogPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_QUEST_LOG;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public List<QuestLogEntryDto> Quests { get; set; } = [];
    [ProtoMember(2)] public List<uint> CompletedQuestIds { get; set; } = [];

    public static NetworkPacket Create(List<QuestLogEntryDto> quests, List<uint> completed, EncryptFunc encrypt)
        => PacketSerializationHelper.Serialize(new SQuestLogPacket { Quests = quests, CompletedQuestIds = completed },
            PacketType, Flags, Protocol, encrypt);
}
