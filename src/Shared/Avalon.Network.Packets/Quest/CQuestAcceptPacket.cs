using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.Quest;

/// <summary>Accepts a quest from its giver, in an open conversation with it (#433). Answered with SMSG_QUEST_RESULT.</summary>
[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_QUEST_ACCEPT)]
public class CQuestAcceptPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_QUEST_ACCEPT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public uint QuestId { get; set; }

    /// <summary>Raw ObjectGuid of the NPC the conversation is with.</summary>
    [ProtoMember(2)] public ulong NpcGuid { get; set; }
}
