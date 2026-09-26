using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.Vendor;

/// <summary>
/// Client to server: sell some or all of the stack in one Bag slot to the open shop. Nothing is ever
/// sold from Equipment or the Bank. Answered with exactly one SVendorResultPacket; the sold item
/// joins the buyback list in the next SVendorListPacket.
/// </summary>
[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_VENDOR_SELL)]
public class CVendorSellPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_VENDOR_SELL;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    /// <summary>Chosen by the client and echoed in the result.</summary>
    [ProtoMember(1)] public uint RequestId { get; set; }

    /// <summary>The Bag slot, 0-29.</summary>
    [ProtoMember(2)] public uint BagSlot { get; set; }

    /// <summary>How many to sell. Absent means the whole stack; 0 is refused as InvalidCount.</summary>
    [ProtoMember(3)] public uint? Count { get; set; }
}
