using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Vendor;

/// <summary>
/// What an open shop offers this player, and what this player can buy back. Always the whole
/// list, never a change. Sent when the shop opens, and again whenever it changes for this player:
/// a stock count, a restock, a reload, or this player's own buyback. The shop closes with
/// SMSG_DIALOGUE_END, as the bank does.
/// </summary>
[ProtoContract]
public class SVendorListPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_VENDOR_LIST;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    /// <summary>The vendor NPC's ObjectGuid, as SMSG_DIALOGUE_NODE named it.</summary>
    [ProtoMember(1)] public ulong VendorGuid { get; set; }

    /// <summary>In Sequence order. Absent on the wire when the vendor sells nothing, as for any empty repeated field.</summary>
    [ProtoMember(2)] public VendorEntryDto[] Entries { get; set; } = [];

    /// <summary>Newest first. Absent on the wire when there is nothing to buy back.</summary>
    [ProtoMember(3)] public VendorBuybackDto[] Buyback { get; set; } = [];

    public static NetworkPacket Create(ulong vendorGuid, VendorEntryDto[] entries, VendorBuybackDto[] buyback, EncryptFunc encrypt)
        => PacketSerializationHelper.Serialize(
            new SVendorListPacket { VendorGuid = vendorGuid, Entries = entries, Buyback = buyback },
            PacketType, Flags, Protocol, encrypt);
}

/// <summary>One row the shop sells.</summary>
[ProtoContract]
public class VendorEntryDto
{
    /// <summary>What CVendorBuyPacket names to buy this row.</summary>
    [ProtoMember(1)] public uint Sequence { get; set; }

    [ProtoMember(2)] public ulong ItemTemplateId { get; set; }

    /// <summary>Copper per unit, before any item costs.</summary>
    [ProtoMember(3)] public ulong Price { get; set; }

    /// <summary>How many are left. Absent means unlimited; 0 means sold out until it restocks.</summary>
    [ProtoMember(4)] public uint? Stock { get; set; }

    /// <summary>Items taken from the Bag per unit bought, on top of the price. Usually none.</summary>
    [ProtoMember(5)] public VendorCostDto[] Costs { get; set; } = [];
}

/// <summary>One item a row costs, per unit bought.</summary>
[ProtoContract]
public class VendorCostDto
{
    [ProtoMember(1)] public ulong ItemTemplateId { get; set; }

    [ProtoMember(2)] public uint Count { get; set; }
}

/// <summary>One sale this session can undo.</summary>
[ProtoContract]
public class VendorBuybackDto
{
    /// <summary>What CVendorBuybackPacket names. 0 is the most recent sale.</summary>
    [ProtoMember(1)] public uint Index { get; set; }

    /// <summary>The exact item sold, in the snapshot's shape. Container is 1 (Bag) and Slot is where it was sold from.</summary>
    [ProtoMember(2)] public ItemSlotDto? Item { get; set; }

    /// <summary>Copper to buy it back: exactly what it sold for.</summary>
    [ProtoMember(3)] public ulong Price { get; set; }
}
