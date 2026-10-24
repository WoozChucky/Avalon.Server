using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Character;
using Avalon.World.Public.Characters;
using Avalon.World.Reload;

namespace Avalon.World.Inventory;

/// <summary>One item on the wire, the same for the login snapshot and the per-tick update.</summary>
public static class ItemSlotDtoMapper
{
    /// <param name="modifiers">
    /// The item's modifiers: from the character's <see cref="ItemModifierStore" /> for a held item, the
    /// <see cref="CarriedItem" />'s own for one outside every container.
    /// </param>
    /// <param name="items">
    /// The current items generation: the template and catalog a magic copy's name is computed from when it is described
    /// (#1179), so a rename reaches every copy. A common or unique copy's name is its template's, and is not sent.
    /// </param>
    public static ItemSlotDto ToDto(InventoryType container, InventoryItem item,
        IReadOnlyList<ItemInstanceModifier> modifiers, ItemsPatch items)
    {
        return new ItemSlotDto
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
            Modifiers = ItemModifierDtoMapper.ToDtos(modifiers),
            Name = NameOf(item.TemplateId, item.Rarity, item.RareName, modifiers, items),
        };
    }

    /// <summary>
    /// A copy's name on the wire (#1179): set for a magic or rare copy that has one, null where the template's name is
    /// the item's, and for a template a reload removed. Shared by every message that describes a copy.
    /// </summary>
    public static string? NameOf(ItemTemplateId templateId, ItemRarity rarity, string? rareName,
        IReadOnlyList<ItemInstanceModifier> modifiers, ItemsPatch items) =>
        rarity is ItemRarity.Magic or ItemRarity.Rare && items.Find(templateId) is { } template
            ? ItemNames.Display(template, rarity, rareName, modifiers, items.Modifiers)
            : null;
}
