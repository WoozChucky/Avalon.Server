using Avalon.Common;

namespace Avalon.Domain.World;

/// <summary>
/// An item's name as it is described (#1179, B-R19). Only a copy's prefix and suffix name it: its implicits and a
/// unique's own modifier never do. Computed whenever the item is described, never stored, so a rename of a base or an
/// affix in the catalog reaches every copy; a rare's two words are the exception, given once by its roll and stored on
/// the copy.
/// </summary>
public static class ItemNames
{
    /// <summary>
    /// The copy's name, or null where the template's name is the item's: a common or unique copy, and a magic copy that
    /// rolled no prefix and no suffix. A magic copy is named <c>&lt;prefix&gt; &lt;template&gt; &lt;suffix&gt;</c> from
    /// its lowest-ordinal prefix and suffix, a missing part dropped; a rare copy by <paramref name="rareName" />. Builds
    /// one string for a magic copy and none otherwise. A modifier the catalog no longer holds names nothing.
    /// </summary>
    public static string? Display(ItemTemplate template, ItemRarity rarity, string? rareName,
        IReadOnlyList<ItemInstanceModifier> modifiers, ItemModifierCatalog catalog)
    {
        switch (rarity)
        {
            case ItemRarity.Rare:
                return rareName;
            case ItemRarity.Magic:
                break;
            default:
                return null;
        }

        string? prefix = null;
        string? suffix = null;
        int prefixOrdinal = int.MaxValue;
        int suffixOrdinal = int.MaxValue;
        for (int i = 0; i < modifiers.Count; i++)
        {
            ItemInstanceModifier rolled = modifiers[i];
            if (catalog.Find(rolled.ModifierId)?.Group is not { } group)
                continue;

            if (group.Kind == ItemModifierKind.Prefix && rolled.Ordinal < prefixOrdinal)
            {
                prefix = group.Name;
                prefixOrdinal = rolled.Ordinal;
            }
            else if (group.Kind == ItemModifierKind.Suffix && rolled.Ordinal < suffixOrdinal)
            {
                suffix = group.Name;
                suffixOrdinal = rolled.Ordinal;
            }
        }

        string name = template.Name;
        return (prefix, suffix) switch
        {
            (null, null) => null,
            ({ } p, null) => string.Concat(p, " ", name),
            (null, { } s) => string.Concat(name, " ", s),
            ({ } p, { } s) => string.Concat(p, " ", name, " ", s),
        };
    }
}
