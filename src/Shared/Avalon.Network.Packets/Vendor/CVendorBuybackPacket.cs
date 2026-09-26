using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.Vendor;

/// <summary>
/// Client to server: buy back one of this session's last ten sales, at the price it sold for. The
/// exact item returns to the lowest free Bag slot. Answered with exactly one SVendorResultPacket.
/// </summary>
[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_VENDOR_BUYBACK)]
public class CVendorBuybackPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_VENDOR_BUYBACK;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    /// <summary>Chosen by the client and echoed in the result.</summary>
    [ProtoMember(1)] public uint RequestId { get; set; }

    /// <summary>The entry's Index in the last SVendorListPacket. 0 is the most recent sale.</summary>
    [ProtoMember(2)] public uint Index { get; set; }
}
