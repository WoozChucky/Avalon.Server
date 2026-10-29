using Avalon.Combat;
using Avalon.Domain.World;

namespace Avalon.Balance.Core;

/// <summary>
/// Gear as fixed stats (#1183): the one copy of a template the simulator wears, every number at the midpoint of its range.
/// It is the world's plain roll (ItemRoller.RollPlain) under a random that lands mid-range, which
/// SimulatorParityShould holds it to: the base stats of the template's ranges, a unique's from its named base, then one
/// modifier per linked modifier, the base's implicit first and a unique's own after it, ordinals from 0.
/// </summary>
public static class FixedGear
{
    /// <summary>
    /// The middle of [<paramref name="min" />, <paramref name="max" />], a half rounded up: what a uniform draw of the range
    /// returns when its random lands mid-range (<c>(lo + hiExclusive) / 2</c> of <c>NextInt64(min, max + 1)</c>).
    /// </summary>
    public static int Midpoint(int min, int max) => (int)(((long)min + max + 1) / 2);

    /// <summary>
    /// The fixed copy of <paramref name="template" />. <paramref name="named" /> is a unique's named base, which gives it its
    /// base stats, its implicit and its damage type; null for any other template.
    /// </summary>
    internal static WornItem Of(ItemTemplate template, ItemTemplate? named, ItemModifierCatalog catalog)
    {
        ItemTemplate source = named ?? template;
        ItemBaseStats baseStats = default;
        foreach (ItemTemplateBaseStat range in source.BaseStats)
            baseStats = baseStats.With(range.Stat, Midpoint(range.Min, range.Max));

        IReadOnlyList<ItemModifier> inherited = named is null ? [] : catalog.LinkedTo(named.Id);
        IReadOnlyList<ItemModifier> links = catalog.LinkedTo(template.Id);
        List<ItemInstanceModifier>? modifiers = null;
        if (inherited.Count + links.Count > 0)
        {
            modifiers = new List<ItemInstanceModifier>(inherited.Count + links.Count);
            foreach (ItemModifier modifier in inherited.Concat(links))
                modifiers.Add(Copy(modifier, (byte)modifiers.Count));
        }

        return new WornItem(template, baseStats, modifiers, WornItem.DamageTypeOf(template, named));
    }

    /// <summary>
    /// One modifier at <paramref name="ordinal" />: a value per stat line, in index order, at the line's midpoint; a line
    /// with a second range also takes its high end, raised to the low end when below it.
    /// </summary>
    private static ItemInstanceModifier Copy(ItemModifier modifier, byte ordinal)
    {
        var row = new ItemInstanceModifier { ModifierId = modifier.Id, Ordinal = ordinal };
        foreach (ItemModifierStat line in modifier.Stats.OrderBy(l => l.Index))
        {
            int value = Midpoint(line.Min, line.Max);
            row.Values.Add(new ItemInstanceModifierValue
            {
                Index = line.Index,
                Stat = line.Stat,
                Value = value,
                Value2 = line.Min2 is { } min2 && line.Max2 is { } max2 ? Math.Max(Midpoint(min2, max2), value) : null,
            });
        }

        return row;
    }
}
