using Avalon.Common;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Items;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Character;

/// <summary>
/// The whole of what a character carries, and its gold, sent once on login. SInventoryUpdatePacket
/// carries later changes as absolute slot values.
/// </summary>
[ProtoContract]
public class SInventorySnapshotPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_INVENTORY_SNAPSHOT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    /// <summary>
    /// protobuf-net writes nothing for a zero-length repeated field, so a character carrying
    /// nothing round-trips this as null, not an empty array, despite the property being declared
    /// non-nullable. A reader must treat Items == null the same as "no items".
    /// </summary>
    [ProtoMember(1)] public ItemSlotDto[] Items { get; set; }

    /// <summary>
    /// The character's gold (Character.Gold). The next free field, so a client that
    /// predates it reads the rest of the packet unchanged.
    /// </summary>
    [ProtoMember(2)] public ulong Gold { get; set; }

    public static OutboundPacket Create(ItemSlotDto[] items, ulong gold, PacketEncoder encoder)
    {
        SInventorySnapshotPacket message = PacketEncoder.Scratch<SInventorySnapshotPacket>();
        message.Items = items;
        message.Gold = gold;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }
}

[ProtoContract]
public class ItemSlotDto
{
    /// <summary>The container the item is in.</summary>
    [ProtoMember(1)] public InventoryType Container { get; set; }

    /// <summary>The slot in that container; a byte, as no container has 255 slots (#921).</summary>
    [ProtoMember(2)] public byte Slot { get; set; }

    /// <summary>What the item is. ItemTemplateId is a ValueObject&lt;ulong&gt;, not a uint.</summary>
    [ProtoMember(3)] public ulong ItemTemplateId { get; set; }

    /// <summary>
    /// Which item it is, for addressing one that a later move would shift out of its slot. Emits
    /// .bcl.Guid, as the three InstanceId fields already on the wire do; a second Guid encoding
    /// would cost every client implementer a rule about which fields use which.
    /// </summary>
    [ProtoMember(4)] public Guid ItemInstanceId { get; set; }

    [ProtoMember(5)] public uint Count { get; set; }

    [ProtoMember(6)] public uint Durability { get; set; }

    /// <summary>ItemInstanceFlags.</summary>
    [ProtoMember(7)] public uint Flags { get; set; }

    /// <summary>The copy's rarity (#1179).</summary>
    [ProtoMember(8)] public ItemRarity Rarity { get; set; }

    /// <summary>The copy's item level (#1179).</summary>
    [ProtoMember(9)] public uint ItemLevel { get; set; }

    /// <summary>
    /// The modifiers the copy rolled (#1179), in the order it shows them; null for an item without any, which is what an
    /// empty repeated field reads back as.
    /// </summary>
    [ProtoMember(10)] public List<ItemModifierDto>? Modifiers { get; set; }
}
