using Avalon.Domain.World;
using Avalon.World.Public.Characters;

namespace Avalon.World.Inventory;

/// <summary>
/// An item outside every container (#1179): taken out for a sale and kept for buyback, or planned into a trade. It
/// carries its modifiers with it, since <see cref="ItemModifierStore" /> holds only what a character's containers hold.
/// </summary>
/// <param name="Item">The item as it was held; its Slot is where it was, or where a trade will put it.</param>
/// <param name="Modifiers">Its modifiers in ordinal order, empty for none.</param>
public readonly record struct CarriedItem(InventoryItem Item, IReadOnlyList<ItemInstanceModifier> Modifiers);
