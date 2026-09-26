using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Character;

/// <summary>
/// What changed in a character's equipment, bag and gold during one tick, as absolute values: each
/// entry is a slot's final value when the tick ended, and Money, when present, is the new balance.
/// A lost or duplicated packet therefore cannot leave the client drifting. At most one flush per
/// connection per tick, plus the bank-open snapshot (every Bank slot, sent when a banker's dialogue
/// opens the bank). Bank slots are included only while the bank is open.
/// </summary>
[ProtoContract]
public class SInventoryUpdatePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_INVENTORY_UPDATE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    /// <summary>Null on the wire when only the money changed, as for any empty repeated field.</summary>
    [ProtoMember(1)] public InventorySlotUpdateDto[] Slots { get; set; }

    /// <summary>The new balance in copper. Absent when the balance did not change this tick.</summary>
    [ProtoMember(2)] public ulong? Money { get; set; }

    public static NetworkPacket Create(InventorySlotUpdateDto[] slots, ulong? money, EncryptFunc encrypt)
        => PacketSerializationHelper.Serialize(
            new SInventoryUpdatePacket { Slots = slots, Money = money },
            PacketType, Flags, Protocol, encrypt);
}

[ProtoContract]
public class InventorySlotUpdateDto
{
    /// <summary>InventoryType, numeric, as on ItemSlotDto.</summary>
    [ProtoMember(1)] public ushort Container { get; set; }

    [ProtoMember(2)] public ushort Slot { get; set; }

    /// <summary>
    /// What the slot now holds, in the same shape as the snapshot's items (its Container and Slot
    /// repeat the ones above). Absent means the slot is now empty.
    /// </summary>
    [ProtoMember(3)] public ItemSlotDto? Item { get; set; }
}
