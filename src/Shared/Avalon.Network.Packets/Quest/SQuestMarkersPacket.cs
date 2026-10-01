using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Quest;

/// <summary>Every quest NPC in the character's instance with its marker for that character (#433), whole, when it changed.</summary>
[ProtoContract]
public class SQuestMarkersPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_QUEST_MARKERS;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public List<QuestMarkerDto> Markers { get; set; } = [];

    public static NetworkPacket Create(List<QuestMarkerDto> markers, EncryptFunc encrypt)
        => PacketSerializationHelper.Serialize(new SQuestMarkersPacket { Markers = markers }, PacketType, Flags, Protocol, encrypt);
}
