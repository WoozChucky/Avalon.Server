using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Talents;

/// <summary>The one answer to a CTalentAllocatePacket (#1181), sent to the requester only.</summary>
[ProtoContract]
public sealed class STalentResultPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_TALENT_RESULT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    /// <summary>The request's RequestId, echoed back.</summary>
    [ProtoMember(1)] public uint RequestId { get; set; }

    [ProtoMember(2)] public TalentResult Result { get; set; }

    /// <summary>The node a refusal names (0 for Ok, Dead, TooMany and InternalError).</summary>
    [ProtoMember(3)] public uint NodeId { get; set; }

    public static OutboundPacket Create(uint requestId, TalentResult result, uint nodeId, PacketEncoder encoder)
    {
        STalentResultPacket packet = PacketEncoder.Scratch<STalentResultPacket>();
        packet.RequestId = requestId;
        packet.Result = result;
        packet.NodeId = nodeId;
        return encoder.Encode(packet, PacketType, Flags, Protocol);
    }
}
