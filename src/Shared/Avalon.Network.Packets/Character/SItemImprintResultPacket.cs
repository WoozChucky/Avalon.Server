using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Character;

/// <summary>The one answer to a CItemImprintPacket (#1180), sent to the requester only.</summary>
[ProtoContract]
public class SItemImprintResultPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_ITEM_IMPRINT_RESULT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    /// <summary>The request's RequestId, echoed back.</summary>
    [ProtoMember(1)] public uint RequestId { get; set; }

    [ProtoMember(2)] public ItemImprintResult Result { get; set; }

    public static OutboundPacket Create(uint requestId, ItemImprintResult result, PacketEncoder encoder)
    {
        SItemImprintResultPacket packet = PacketEncoder.Scratch<SItemImprintResultPacket>();
        packet.RequestId = requestId;
        packet.Result = result;
        return encoder.Encode(packet, PacketType, Flags, Protocol);
    }
}
