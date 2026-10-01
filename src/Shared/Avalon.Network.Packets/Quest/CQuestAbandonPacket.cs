using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.Quest;

/// <summary>Drops a held quest, anywhere (#433); its quest items leave the bag and the bank. Answered with SMSG_QUEST_RESULT.</summary>
[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_QUEST_ABANDON)]
public class CQuestAbandonPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_QUEST_ABANDON;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public uint QuestId { get; set; }
}
