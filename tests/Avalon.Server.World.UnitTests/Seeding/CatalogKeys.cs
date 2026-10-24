using Avalon.Database.World.Seeding;
using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;

namespace Avalon.Server.World.UnitTests.Seeding;

/// <summary>
/// Names a seeded catalog modifier the way the catalog does (#1179), so no test holds an id: "P2 T2" (a family's tier),
/// "P8 Fire T1" or "S5 Blight T2" (an element of a split family), "unique 65" (the unique template's own modifier) and
/// "implicit 37" (the base template's implicit row).
/// </summary>
internal static class CatalogKeys
{
    public static int ModifierId(string key)
    {
        string[] parts = key.Split(' ');
        ItemModifierGroup[] groups = ItemModifierSeed.Groups();
        ItemModifier[] modifiers = ItemModifierSeed.Modifiers();

        if (parts[0] is "unique" or "implicit")
        {
            ItemModifierKind kind = parts[0] == "unique" ? ItemModifierKind.Unique : ItemModifierKind.Implicit;
            ulong template = ulong.Parse(parts[1]);
            ItemTemplateModifier link = ItemModifierSeed.TemplateLinks().Single(l => l.TemplateId.Value == template);
            ItemModifier linked = modifiers.Single(m => m.Id == link.ModifierId);
            Assert.Equal(kind, groups.Single(g => g.Id == linked.GroupId).Kind);
            return linked.Id;
        }

        DamageType? element = parts.Length == 3 ? Enum.Parse<DamageType>(parts[1]) : null;
        byte tier = byte.Parse(parts[^1].TrimStart('T'));
        ItemModifierGroup group = groups.Single(g => g.Code == parts[0] && g.Element == element);
        return modifiers.Single(m => m.GroupId == group.Id && m.Tier == tier).Id;
    }
}
