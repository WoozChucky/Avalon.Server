using Avalon.Common;
using ProtoBuf;

namespace Avalon.Network.Packets.Items;

/// <summary>
/// What one item instance is beyond its template (#1179): its rarity, its item level and the modifiers it rolled. Sent
/// with every item a client is shown in a slot or a trade window, always set.
/// </summary>
[ProtoContract]
public class ItemDetailsDto
{
    [ProtoMember(1)] public ItemRarity Rarity { get; set; }

    [ProtoMember(2)] public uint ItemLevel { get; set; }

    /// <summary>
    /// In the order the item shows them. protobuf-net writes nothing for an empty repeated field, so an item without
    /// modifiers reads back with an empty list.
    /// </summary>
    [ProtoMember(3)] public List<ItemModifierDto> Modifiers { get; set; } = [];
}
