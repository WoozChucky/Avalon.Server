using Avalon.Common;
using Avalon.Domain.World;
using Avalon.Network.Packets.Loot;
using Avalon.World.Inventory;

namespace Avalon.World.Loot;

public static class LootDropMapper
{
    /// <summary>
    /// <paramref name="drop" /> on the wire, its modifiers in lists of its own: for a snapshot, built once for every
    /// connection it is sent to. FreeForAllAt goes out as UTC ticks, the clock SPingPacket.ServerTimestamp carries.
    /// </summary>
    /// <param name="data">Gives an item drop that did not roll its template's item level.</param>
    public static LootDropDto ToDto(GroundLoot drop, StaticData data)
    {
        LootDropDto dto = FillDrop(new LootDropDto(), drop, data);
        dto.Modifiers = drop.Rolled is { } rolled ? ItemModifierDtoMapper.ToDtos(rolled.Modifiers) : null;
        return dto;
    }

    /// <summary>
    /// Writes <paramref name="drop" /> over <paramref name="dto" />, every member of it, so a message kept from an
    /// earlier drop (a kill refills the ones its instance keeps, #890) carries nothing of that drop: its modifiers are
    /// refilled in place through <paramref name="pool" /> (#1179), which keeps what a smaller drop leaves over.
    /// </summary>
    /// <param name="data">Gives an item drop that did not roll its template's item level.</param>
    public static LootDropDto Fill(LootDropDto dto, GroundLoot drop, StaticData data, ItemModifierDtoPool pool)
    {
        FillDrop(dto, drop, data);
        // Each branch its own call: under one conditional the empty collection would be made a new list, one per drop.
        dto.Modifiers = drop.Rolled is { } rolled
            ? pool.Refill(dto.Modifiers, rolled.Modifiers)
            : pool.Refill(dto.Modifiers, []);
        return dto;
    }

    /// <summary>
    /// Every member but the modifiers. A rolled copy sends its own rarity, item level and name (#1179); an item drop that
    /// did not roll sends Common, the item level a copy made from its template outright takes, which its pickup makes,
    /// and no name; a gold pile, Common and 0.
    /// </summary>
    private static LootDropDto FillDrop(LootDropDto dto, GroundLoot drop, StaticData data)
    {
        dto.LootGuid = drop.Guid.RawValue;
        dto.Position.X = drop.Position.x;
        dto.Position.Y = drop.Position.y;
        dto.Position.Z = drop.Position.z;
        dto.ItemTemplateId = drop.ItemTemplateId?.Value;
        dto.Count = drop.Count;
        dto.Gold = drop.IsGold ? drop.Gold : null;
        dto.OwnerCharacterId = drop.OwnerCharacterId;
        dto.FreeForAllAt = drop.FreeForAllAt.Ticks;
        if (drop.Rolled is { } rolled)
        {
            dto.Rarity = rolled.Rarity;
            dto.ItemLevel = rolled.ItemLevel;
            dto.Name = drop.NameIn(data.Items);
        }
        else
        {
            dto.Rarity = ItemRarity.Common;
            dto.ItemLevel = drop.ItemTemplateId is { } id ? ItemLevelOf(data.FindItemTemplate(id)) : 0u;
            dto.Name = null;
        }

        return dto;
    }

    /// <summary>A template a reload removed since the drop was rolled cannot be made: 1, the least an item level is.</summary>
    private static uint ItemLevelOf(ItemTemplate? template) =>
        template is null ? 1u : ItemInstanceDefaults.InitialItemLevel(template);
}
