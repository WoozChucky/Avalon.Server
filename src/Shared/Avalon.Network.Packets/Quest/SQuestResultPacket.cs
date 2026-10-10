using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Quest;

/// <summary>Exactly one per quest request (#433), to the requester. A request that threw is answered Error.</summary>
[ProtoContract]
public class SQuestResultPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_QUEST_RESULT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public QuestResult Result { get; set; }
    [ProtoMember(2)] public uint QuestId { get; set; }

    public static OutboundPacket Create(QuestResult result, uint questId, PacketEncoder encoder)
    {
        SQuestResultPacket message = PacketEncoder.Scratch<SQuestResultPacket>();
        message.Result = result;
        message.QuestId = questId;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }
}
