using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Items;
using Avalon.World.Public.Characters;
using Avalon.World.Reload;

namespace Avalon.World.Inventory;

/// <summary>One item on the wire, the same for the login snapshot, the per-tick update, a refusal and the bank.</summary>
public static class ItemSlotDtoMapper
{
    /// <summary>
    /// A held item: its modifiers and base stats from the character's <see cref="ItemModifierStore" />, their entries
    /// taken from <paramref name="pool" /> (#1179), and the name the store holds for it. The caller gives the entries back
    /// (<see cref="ItemModifierDtoPool.Return(ItemSlotDto?)" />) once the message carrying them is encoded, so a message
    /// sent again and again (the tick's inventory update) makes no modifier or base-stat garbage.
    /// </summary>
    /// <param name="items">
    /// The current items generation: the template and catalog a magic copy's name is worked out from, once per
    /// generation, so a rename reaches every copy. A common or unique copy's name is its template's, and is not sent.
    /// </param>
    public static ItemSlotDto ToDto(InventoryType container, InventoryItem item, ItemModifierStore modifiers,
        ItemsPatch items, ItemModifierDtoPool pool) =>
        Slot(container, item, pool.Rent(modifiers.Of(item.InstanceId)),
            pool.RentBaseStats(modifiers.BaseStatsOf(item.InstanceId)), modifiers.NameOf(item, items));

    /// <summary>
    /// An item outside every container (a sale kept for buyback), for a message built once: its modifiers and base stats
    /// in entries of their own, and its name worked out now.
    /// </summary>
    public static ItemSlotDto ToDto(InventoryType container, CarriedItem carried, ItemsPatch items) =>
        Slot(container, carried.Item, ItemModifierDtoMapper.ToDtos(carried.Modifiers),
            ItemModifierDtoMapper.ToBaseStatDtos(carried.BaseStats),
            NameOf(carried.Item.TemplateId, carried.Item.Rarity, carried.Item.RareName, carried.Modifiers, items));

    /// <summary>
    /// A copy's name on the wire (#1179), the one rule every message that describes a copy follows: a rare copy's stored
    /// name (kept even when a reload removed its template), a magic copy's worked out from its template and its prefix and
    /// suffix (<see cref="ItemNames.Display" />; null when its template is gone or it rolled neither), and null for a
    /// common or unique copy, whose template's name is the item's. The world holds a held copy's and a drop's name
    /// (<see cref="DescribedName" />) rather than calling this at every describe.
    /// </summary>
    public static string? NameOf(ItemTemplateId templateId, ItemRarity rarity, string? rareName,
        IReadOnlyList<ItemInstanceModifier> modifiers, ItemsPatch items) => rarity switch
        {
            ItemRarity.Rare => rareName,
            ItemRarity.Magic when items.Find(templateId) is { } template =>
                ItemNames.Display(template, rarity, rareName, modifiers, items.Modifiers),
            _ => null,
        };

    private static ItemSlotDto Slot(InventoryType container, InventoryItem item, List<ItemModifierDto>? modifiers,
        List<ItemBaseStatDto>? baseStats, string? name) => new()
        {
            Container = container,
            Slot = item.Slot,
            ItemTemplateId = item.TemplateId.Value,
            ItemInstanceId = item.InstanceId.Value,
            Count = item.Count,
            Durability = item.Durability,
            Flags = (uint)item.Flags,
            Rarity = item.Rarity,
            ItemLevel = item.ItemLevel,
            Modifiers = modifiers,
            Name = name,
            BaseStats = baseStats,
        };
}
