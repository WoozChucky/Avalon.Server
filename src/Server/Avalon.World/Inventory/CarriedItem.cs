using Avalon.Domain.World;
using Avalon.World.Public.Characters;

namespace Avalon.World.Inventory;

/// <summary>
/// An item outside every container (#1179): taken out for a sale and kept for buyback, or planned into a trade. It
/// carries its rolled rows with it, since <see cref="ItemModifierStore" /> holds only what a character's containers hold.
/// </summary>
/// <param name="Item">The item as it was held; its Slot is where it was, or where a trade will put it.</param>
/// <param name="BaseStats">Its rolled base stats, by value; none (default) for an item without them.</param>
/// <param name="Modifiers">Its modifiers in ordinal order, empty for none.</param>
public readonly record struct CarriedItem(InventoryItem Item, ItemBaseStats BaseStats, IReadOnlyList<ItemInstanceModifier> Modifiers)
{
    /// <summary>
    /// Whether it carries rolled rows, modifiers or base stats (<see cref="ItemModifierStore.HasRolledRows" />).
    /// </summary>
    public bool HasRolledRows => ItemModifierStore.HasRolledRows(BaseStats, Modifiers);
}
