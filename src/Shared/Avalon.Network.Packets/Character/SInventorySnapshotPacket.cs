using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Character;

/// <summary>
/// The whole of what a character carries, sent once on login. "Snapshot" rather than "inventory"
/// because a later update packet will carry deltas against this.
/// </summary>
[ProtoContract]
public class SInventorySnapshotPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_INVENTORY_SNAPSHOT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public ItemSlotDto[] Items { get; set; }

    public static NetworkPacket Create(ItemSlotDto[] items, EncryptFunc encrypt)
        => PacketSerializationHelper.Serialize(
            new SInventorySnapshotPacket { Items = items },
            PacketType, Flags, Protocol, encrypt);
}

[ProtoContract]
public class ItemSlotDto
{
    /// <summary>InventoryType. Crosses as a number, as Class does on SCharacterSelectedPacket.</summary>
    [ProtoMember(1)] public ushort Container { get; set; }

    [ProtoMember(2)] public ushort Slot { get; set; }

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
}
