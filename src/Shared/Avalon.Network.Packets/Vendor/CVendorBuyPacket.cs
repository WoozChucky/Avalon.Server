using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.Vendor;

/// <summary>
/// Client to server: buy from one row of the open shop's list. Answered with exactly one
/// SVendorResultPacket. The gold and the item arrive in the tick's SInventoryUpdatePacket, and a
/// stock change in SVendorListPacket. Only valid while a vendor conversation that chose
/// "Show me your wares." is still open and within 15 m of the vendor.
/// </summary>
[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_VENDOR_BUY)]
public class CVendorBuyPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_VENDOR_BUY;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    /// <summary>Chosen by the client and echoed in the result, so it can pair answers with requests.</summary>
    [ProtoMember(1)] public uint RequestId { get; set; }

    /// <summary>The row's Sequence, as SVendorListPacket listed it.</summary>
    [ProtoMember(2)] public uint Sequence { get; set; }

    /// <summary>How many to buy. Absent means 1. 0, or more than the item's MaxStackSize, is refused as InvalidCount.</summary>
    [ProtoMember(3)] public uint? Count { get; set; }
}
