using Avalon.Database.World.Seeding;
using Avalon.Domain.World;

namespace Avalon.Server.World.UnitTests.Seeding;

/// <summary>
/// Names a seeded catalog modifier the way the catalog does (#1179), so no test holds an id: "P2 T8" (a family's tier),
/// "P8 Fire T9" or "S5 Blight T8" (an element of a split family), "implicit FireResistance T9" (an implicit group's tier,
/// by its stat) and "unique 65" (the unique template's own modifier).
/// </summary>
internal static class CatalogKeys
{
    /// <summary>Every seeded row by its key, with its group's id and its own.</summary>
    public static IReadOnlyList<(string Key, int GroupId, int ModifierId)> All()
    {
        Dictionary<int, ItemModifierGroup> groups = ItemModifierSeed.Groups().ToDictionary(g => g.Id);
        ItemModifier[] modifiers = ItemModifierSeed.Modifiers();
        HashSet<int> uniques = [.. modifiers.Where(m => groups[m.GroupId].Kind == ItemModifierKind.Unique).Select(m => m.Id)];
        var templateOf = ItemModifierSeed.TemplateLinks()
            .Where(l => uniques.Contains(l.ModifierId))
            .ToDictionary(l => l.ModifierId, l => l.TemplateId.Value);
        ILookup<int, ItemModifierStat> stats = ItemModifierSeed.Stats().ToLookup(s => s.ModifierId);

        return
        [
            .. modifiers.Select(m =>
            {
                ItemModifierGroup group = groups[m.GroupId];
                string key = group.Kind switch
                {
                    ItemModifierKind.Unique => $"unique {templateOf[m.Id]}",
                    ItemModifierKind.Implicit => $"implicit {stats[m.Id].Single().Stat} T{m.Tier}",
                    _ when group.Element is { } element => $"{group.Code} {element} T{m.Tier}",
                    _ => $"{group.Code} T{m.Tier}",
                };
                return (key, group.Id, m.Id);
            }),
        ];
    }

    public static int ModifierId(string key) => All().Single(row => row.Key == key).ModifierId;
}
