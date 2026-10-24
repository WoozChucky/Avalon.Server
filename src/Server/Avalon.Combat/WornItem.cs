using Avalon.Domain.World;

namespace Avalon.Combat;

/// <summary>
/// One item a character wears (#1179), as the stats calculation reads it: its template's stat pairs, the base stats this
/// copy rolled when it was made (its armour, evasion, ward, block, or a weapon's damage and attack time; none for
/// jewellery) and the modifiers it rolled, each value naming its stat, or null when it rolled none. The template is the
/// current one, but the base stats and the modifiers belong to the instance, so a range edited live changes new copies
/// only. The base stats are a value, read in place; the list is the concrete one its owner holds, so a fold reads it
/// without an interface call.
/// </summary>
public readonly record struct WornItem(ItemTemplate Template, ItemBaseStats BaseStats, List<ItemInstanceModifier>? Modifiers);
