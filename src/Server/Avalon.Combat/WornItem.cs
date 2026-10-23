using Avalon.Domain.World;

namespace Avalon.Combat;

/// <summary>
/// One item a character wears (#1179), as the stats calculation reads it: its template's base lines (damage, attack time,
/// stat pairs) and the modifiers this copy rolled, each value naming its stat. The template is the current one, so a
/// base edited live reaches every worn copy, while the modifiers belong to the instance.
/// </summary>
public readonly record struct WornItem(ItemTemplate Template, IReadOnlyList<ItemInstanceModifier> Modifiers);
