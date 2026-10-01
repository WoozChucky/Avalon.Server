using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.Quest;

/// <summary>Hands a ready quest in to its ender, in an open conversation with it (#433). Answered with SMSG_QUEST_RESULT.</summary>
[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_QUEST_TURN_IN)]
public class CQuestTurnInPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_QUEST_TURN_IN;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public uint QuestId { get; set; }

    /// <summary>Raw ObjectGuid of the NPC the conversation is with.</summary>
    [ProtoMember(2)] public ulong NpcGuid { get; set; }
}
