using Avalon.Common;
using Avalon.Network.Packets.Items;
using Avalon.Network.Packets.World;
using ProtoBuf;

namespace Avalon.Network.Packets.Loot;

/// <summary>
/// One drop on the ground: a stack of one item template, or a pile of gold. Exactly one of
/// <see cref="ItemTemplateId"/> and <see cref="Gold"/> is present.
/// </summary>
[ProtoContract]
public class LootDropDto
{
    /// <summary>Raw ObjectGuid, of type Loot. What CLootPickupPacket names.</summary>
    [ProtoMember(1)] public ulong LootGuid { get; set; }

    /// <summary>World position, already on the ground.</summary>
    // The wire contract stays as it is: IsRequired would change what is serialized (WireSchemaShould pins it).
#pragma warning disable PBN0022
    [ProtoMember(2)] public Vector3Dto Position { get; set; } = new();
#pragma warning restore PBN0022

    /// <summary>Present for an item drop.</summary>
    [ProtoMember(3)] public ulong? ItemTemplateId { get; set; }

    /// <summary>How many of the item; 0 for a gold pile. Never more than the template's stack size.</summary>
    [ProtoMember(4)] public uint Count { get; set; }

    /// <summary>Present for a gold pile: its gold.</summary>
    [ProtoMember(5)] public ulong? Gold { get; set; }

    /// <summary>
    /// The character this drop is reserved for until <see cref="FreeForAllAt"/>. Absent when anyone
    /// may take it.
    /// </summary>
    [ProtoMember(6)] public uint? OwnerCharacterId { get; set; }

    /// <summary>
    /// When anyone may take it, as UTC ticks: the clock SPingPacket.ServerTimestamp carries. The
    /// client decides "yours" or "anyone's" from this and <see cref="OwnerCharacterId"/>; no packet
    /// is sent when the time passes.
    /// </summary>
    [ProtoMember(7)] public long FreeForAllAt { get; set; }

    /// <summary>
    /// The copy's rarity (#1179): what the drop rolled; Common for an item drop that does not roll, and for a gold pile.
    /// </summary>
    [ProtoMember(8)] public ItemRarity Rarity { get; set; }

    /// <summary>
    /// The copy's item level (#1179): what the drop rolled at; the template's required level, or 1, for an item drop that
    /// does not roll; 0 for a gold pile.
    /// </summary>
    [ProtoMember(9)] public uint ItemLevel { get; set; }

    /// <summary>
    /// The modifiers the copy rolled (#1179), in the order it shows them; null for a drop without any, which is what an
    /// empty repeated field reads back as. On the ground a copy is identified exactly when it is Common: any other drop
    /// sends its implicit line only, and no drop sends a name (a client shows its template's).
    /// </summary>
    [ProtoMember(10)] public List<ItemModifierDto>? Modifiers { get; set; }

    /// <summary>
    /// The base stats the copy rolled (#1179), in base-stat order; null for a drop without any (jewellery, a consumable,
    /// a gold pile), which is what an empty repeated field reads back as.
    /// </summary>
    [ProtoMember(12)] public List<ItemBaseStatDto>? BaseStats { get; set; }
}
