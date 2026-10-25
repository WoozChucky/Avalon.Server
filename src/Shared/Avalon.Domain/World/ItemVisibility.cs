using Avalon.Common;

namespace Avalon.Domain.World;

/// <summary>
/// What a client is shown of an item copy (#1179), the one rule the world server's messages and the REST inventory both
/// follow. An identified copy shows every modifier. An unidentified one (magic, rare or unique) shows its base stats and
/// its implicit lines only: never a prefix, a suffix or a unique's own line, and no generated name. A copy a drop or a
/// quest rolls arrives identified exactly when it is Common.
/// </summary>
public static class ItemVisibility
{
    /// <summary>
    /// Whether a copy rolled at <paramref name="rarity" /> (a drop, on the ground and once picked up, or a quest reward)
    /// arrives identified: exactly when it is Common, so the ground needs no field beyond the drop's rarity.
    /// </summary>
    public static bool IdentifiedWhenRolled(ItemRarity rarity) => rarity == ItemRarity.Common;

    /// <summary>
    /// Whether an unidentified copy shows the modifier whose catalog row is <paramref name="catalogRow" />: only an
    /// implicit line, by its kind, never by its ordinal. A modifier the catalog does not hold (null) is hidden, so a
    /// prefix, a suffix or a unique's own line never reaches a client before the copy is identified.
    /// </summary>
    public static bool ShowsUnidentified(ItemModifier? catalogRow) =>
        catalogRow?.Group.Kind == ItemModifierKind.Implicit;

    /// <summary>
    /// Whether a copy shows modifier <paramref name="modifierId" />: always when <paramref name="identified" />, with no
    /// lookup, and otherwise by its row in <paramref name="catalog" /> (<see cref="ShowsUnidentified" />). Allocation-free.
    /// </summary>
    public static bool Shows(ItemModifierCatalog catalog, int modifierId, bool identified) =>
        identified || ShowsUnidentified(catalog.Find(modifierId));
}
