using Avalon.Common;
using Avalon.Domain.World;
using Avalon.Network.Packets.Character;
using Avalon.World.Public.Characters;

namespace Avalon.World.Inventory;

/// <summary>One item on the wire, the same for the login snapshot and the per-tick update.</summary>
public static class ItemSlotDtoMapper
{
    /// <param name="modifiers">
    /// The item's modifiers: from the character's <see cref="ItemModifierStore" /> for a held item, the
    /// <see cref="CarriedItem" />'s own for one outside every container.
    /// </param>
    public static ItemSlotDto ToDto(InventoryType container, InventoryItem item,
        IReadOnlyList<ItemInstanceModifier> modifiers)
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
        };
    }
}
