using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Inventory;

namespace Avalon.World.Loot;

/// <summary>
/// Makes copies of items with their modifiers (#1179). A copy rolls when it is made, on the tick, in the purchase, use,
/// reward, creation or kill that makes it; nothing rolls per tick or per hit.
/// </summary>
public interface IItemRoller
{
    /// <summary>
    /// A plain copy of <paramref name="template" /> at <paramref name="itemLevel" />: its template's rarity and only the
    /// modifiers every copy of it rolls (B-R12), one per link of <paramref name="catalog" />, ordinals from 0 in link order,
    /// each value uniform within its line's range. A template that links nothing makes a copy with none.
    /// </summary>
    RolledItem RollPlain(ItemTemplate template, ushort itemLevel, ItemModifierCatalog catalog);
}

/// <summary>
/// <see cref="IItemRoller" /> over the loot random and the server's id allocator: the copy's id and each modifier's id
/// are handed out at the roll, so nothing is renamed when the copy is added. Stateless, so one serves every thread.
/// </summary>
public sealed class ItemRoller(ILootRandom random, IItemIdAllocator itemIds) : IItemRoller
{
    public RolledItem RollPlain(ItemTemplate template, ushort itemLevel, ItemModifierCatalog catalog)
    {
        ItemInstanceId instanceId = itemIds.Next();
        IReadOnlyList<ItemModifier> links = catalog.LinkedTo(template.Id);
        if (links.Count == 0)
            return new RolledItem(instanceId, template.Id, template.Rarity, itemLevel, RareName: null, []);

        // A list, not an array: the character's modifier store keeps it as the copy's own entry (ItemModifierStore.PutMade).
        var modifiers = new List<ItemInstanceModifier>(links.Count);
        for (int ordinal = 0; ordinal < links.Count; ordinal++)
            modifiers.Add(Roll(instanceId, links[ordinal], (byte)ordinal));

        return new RolledItem(instanceId, template.Id, template.Rarity, itemLevel, RareName: null, modifiers);
    }

    /// <summary>
    /// One modifier on the copy: a value per stat line, in the line's index, uniform in [Min, Max]; a line with a second
    /// range also rolls its high end uniform in [Min2, Max2], raised to the low end when below it (B-R15).
    /// </summary>
    private ItemInstanceModifier Roll(ItemInstanceId instanceId, ItemModifier modifier, byte ordinal)
    {
        Guid id = itemIds.NextModifierId();
        var values = new List<ItemInstanceModifierValue>(modifier.Stats.Count);
        foreach (ItemModifierStat line in modifier.Stats)
        {
            int value = Uniform(line.Min, line.Max);
            int? high = line.Min2 is { } min2 && line.Max2 is { } max2 ? Math.Max(Uniform(min2, max2), value) : null;
            values.Add(new ItemInstanceModifierValue
            {
                ItemInstanceModifierId = id,
                Index = line.Index,
                Stat = line.Stat,
                Value = value,
                Value2 = high,
            });
        }

        return new ItemInstanceModifier
        {
            Id = id,
            ItemInstanceId = instanceId,
            ModifierId = modifier.Id,
            Ordinal = ordinal,
            Values = values,
        };
    }

    private int Uniform(int min, int max) => (int)random.NextInt64(min, (long)max + 1);
}
