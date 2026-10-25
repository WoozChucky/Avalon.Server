using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;

namespace Avalon.Combat;

/// <summary>
/// One item a character wears (#1179), as the stats calculation reads it: its template's stat pairs, the base stats this
/// copy rolled when it was made (its armour, evasion, ward, block, or a weapon's damage and attack time; none for
/// jewellery), the modifiers it rolled, each value naming its stat, or null when it rolled none, and the type of its
/// damage, which its base gives it (a Common weapon's own, a unique's named base's; null for anything but a weapon). The
/// template is the current one, but the base stats and the modifiers belong to the instance, so a range edited live
/// changes new copies only. The base stats are a value, read in place; the list is the concrete one its owner holds, so a
/// fold reads it without an interface call.
/// </summary>
public readonly record struct WornItem(ItemTemplate Template, ItemBaseStats BaseStats, List<ItemInstanceModifier>? Modifiers,
    DamageType? DamageType)
{
    /// <summary>
    /// The type of the damage a copy of <paramref name="template" /> deals: a unique's is its named base's,
    /// <paramref name="named" /> (none when that base is missing), any other template's its own. A unique carries no type
    /// of its own (<see cref="ItemBaseStatRules.DamageTypeProblem" />).
    /// </summary>
    public static DamageType? DamageTypeOf(ItemTemplate template, ItemTemplate? named) =>
        template.BaseTemplateId is null ? template.DamageType : named?.DamageType;
}
