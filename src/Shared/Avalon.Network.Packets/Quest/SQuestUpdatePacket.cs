using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Quest;

/// <summary>One quest's change (#433), at most once per quest per tick, after both tick passes. Display only on Accepted.</summary>
[ProtoContract]
public class SQuestUpdatePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_QUEST_UPDATE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public uint QuestId { get; set; }
    [ProtoMember(2)] public QuestUpdateKind Kind { get; set; }
    [ProtoMember(3)] public QuestStateKind State { get; set; }
    [ProtoMember(4)] public int Stage { get; set; }
    [ProtoMember(5)] public QuestDisplayDto? Display { get; set; }
    [ProtoMember(6)] public List<QuestProgressDto> Progress { get; set; } = [];

    public static OutboundPacket Create(SQuestUpdatePacket update, PacketEncoder encoder)
        => encoder.Encode(update, PacketType, Flags, Protocol);
}
