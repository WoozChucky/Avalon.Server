using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Character;

/// <summary>The one answer to a CItemExtractPacket (#1180), sent to the requester only.</summary>
[ProtoContract]
public class SItemExtractResultPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_ITEM_EXTRACT_RESULT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    /// <summary>The request's RequestId, echoed back.</summary>
    [ProtoMember(1)] public uint RequestId { get; set; }

    [ProtoMember(2)] public ItemExtractResult Result { get; set; }

    public static OutboundPacket Create(uint requestId, ItemExtractResult result, PacketEncoder encoder)
    {
        SItemExtractResultPacket packet = PacketEncoder.Scratch<SItemExtractResultPacket>();
        packet.RequestId = requestId;
        packet.Result = result;
        return encoder.Encode(packet, PacketType, Flags, Protocol);
    }
}
