using Avalon.World.Public.Characters;

namespace Avalon.World.Items;

/// <summary>
/// The one lock on modifying a copy (#1180 part C): extraction and imprinting change only an item this allows, and a seal
/// fills and wakes only on one (<see cref="Awakening.Fills" />), so a copy whose affixes are not known yet, or one Tainted
/// for good, is never changed by them, and nothing binds a Tainted copy again.
/// </summary>
public static class ItemModification
{
    /// <summary>Whether <paramref name="item" /> may be modified: it is identified and not Tainted.</summary>
    public static bool Allowed(in InventoryItem item) => item.Identified && !item.Tainted;
}
