using Avalon.Domain.World;
using Avalon.World.Loot;

namespace Avalon.Server.World.UnitTests.Loot;

/// <summary>One roll of a creature's drops in a list of its own, for tests that read a roll by itself.</summary>
internal static class LootRolls
{
    public static List<RolledDrop> Roll(this ILootRoller roller, CreatureTemplate template, LootCatalog catalog,
        IReadOnlyCollection<ItemTemplate> items)
    {
        var drops = new List<RolledDrop>();
        roller.Roll(template, catalog, items, drops);
        return drops;
    }
}
