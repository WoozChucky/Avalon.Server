using Avalon.Domain.World;

namespace Avalon.Combat;

/// <summary>
/// One item a character wears (#1179), as the stats calculation reads it: its template's base lines (damage, attack time,
/// stat pairs) and the modifiers this copy rolled, each value naming its stat, or null when it rolled none. The template
/// is the current one, so a base edited live reaches every worn copy, while the modifiers belong to the instance. The
/// list is the concrete one its owner holds, so a fold reads it without an interface call.
/// </summary>
public readonly record struct WornItem(ItemTemplate Template, List<ItemInstanceModifier>? Modifiers);
