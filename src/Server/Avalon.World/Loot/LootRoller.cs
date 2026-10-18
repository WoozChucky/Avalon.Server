using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Reload;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Loot;

/// <summary>
/// Rolls what a dying creature, or a used doodad, drops. Tick thread. It allocates the list it hands back,
/// and nothing else: each entry that hits finds its item template by id.
/// </summary>
public interface ILootRoller
{
    /// <summary>
    /// Item stacks in roll order, then at most one gold pile, always last. Reads
    /// <paramref name="template"/>'s table from <paramref name="catalog"/> as it is now, so a Loot
    /// reload applies to the next kill. It is the roll by table below, over the creature's table and copper range.
    /// </summary>
    IReadOnlyList<RolledDrop> Roll(CreatureTemplate template, LootCatalog catalog, IReadOnlyCollection<ItemTemplate> items);

    /// <summary>
    /// Item stacks from <paramref name="table"/> (none when null) in roll order, then at most one gold pile of
    /// <paramref name="minGold"/> to <paramref name="maxGold"/> copper, always last. Reads the table from
    /// <paramref name="catalog"/> as it is now; a table it does not hold drops only gold.
    /// </summary>
    IReadOnlyList<RolledDrop> Roll(LootTableId? table, int minGold, int maxGold, LootCatalog catalog,
        IReadOnlyCollection<ItemTemplate> items);
}

public sealed class LootRoller(ILootRandom random, ILogger<LootRoller> logger) : ILootRoller
{
    /// <summary>
    /// How many references deep a roll may go; the creature's own table is depth 0. The catalog
    /// refuses cycles, so this is the backstop if one ever got through.
    /// </summary>
    public const int MaxReferenceDepth = 8;

    public IReadOnlyList<RolledDrop> Roll(CreatureTemplate template, LootCatalog catalog, IReadOnlyCollection<ItemTemplate> items) =>
        Roll(template.LootTableId, template.MinGold, template.MaxGold, catalog, items);

    public IReadOnlyList<RolledDrop> Roll(LootTableId? table, int minGold, int maxGold, LootCatalog catalog,
        IReadOnlyCollection<ItemTemplate> items)
    {
        var drops = new List<RolledDrop>();

        if (table is { } tableId)
        {
            if (catalog.TryGet(tableId, out LootTableView? view))
            {
                RollTable(view, 0, catalog, items, drops);
            }
            else
            {
                logger.LogWarning("Loot table {TableId} is missing or was refused; this roll drops only gold", tableId.Value);
            }
        }

        if (RollGold(minGold, maxGold) is { } copper)
            drops.Add(RolledDrop.GoldPile(copper));

        return drops;
    }

    private void RollTable(LootTableView table, int depth, LootCatalog catalog,
        IReadOnlyCollection<ItemTemplate> items, List<RolledDrop> drops)
    {
        if (depth >= MaxReferenceDepth)
        {
            logger.LogWarning("Loot table {TableId} is {Depth} references deep; not rolling it", table.Id.Value, depth);
            return;
        }

        // By index throughout the roll: a kill walks these lists, and a foreach through the interface boxes each
        // enumerator (#880).
        IReadOnlyList<LootEntryView> ungrouped = table.Ungrouped;
        for (int i = 0; i < ungrouped.Count; i++)
        {
            LootEntryView entry = ungrouped[i];
            if (random.NextDouble() * 100.0 < entry.Chance)
                Resolve(table, entry, depth, catalog, items, drops);
        }

        IReadOnlyList<LootGroupView> groups = table.Groups;
        for (int i = 0; i < groups.Count; i++)
        {
            if (PickByWeight(groups[i]) is { } picked)
                Resolve(table, picked, depth, catalog, items, drops);
        }
    }

    /// <summary>Exactly one entry, weighted by Chance; null only when the weights add up to 0.</summary>
    private LootEntryView? PickByWeight(LootGroupView group)
    {
        IReadOnlyList<LootEntryView> entries = group.Entries;
        double total = 0;
        for (int i = 0; i < entries.Count; i++)
            total += Math.Max(0f, entries[i].Chance);

        if (total <= 0)
            return null;

        double pick = random.NextDouble() * total;
        double cumulative = 0;
        LootEntryView? last = null;

        for (int i = 0; i < entries.Count; i++)
        {
            LootEntryView entry = entries[i];
            if (entry.Chance <= 0f)
                continue;

            cumulative += entry.Chance;
            last = entry;
            if (pick < cumulative)
                return entry;
        }

        // Rounding at the very top of the range; the roll still belongs to the last weighted entry.
        return last;
    }

    private void Resolve(LootTableView table, LootEntryView entry, int depth, LootCatalog catalog,
        IReadOnlyCollection<ItemTemplate> items, List<RolledDrop> drops)
    {
        if (entry.ReferenceTableId is { } referenced)
        {
            // The catalog only holds tables whose references resolve, so this always finds one.
            if (catalog.TryGet(referenced, out LootTableView? next))
                RollTable(next, depth + 1, catalog, items, drops);
            return;
        }

        ItemTemplateId itemId = entry.ItemTemplateId!;
        ItemTemplate? item = FindItem(items, itemId);
        if (item is null)
        {
            logger.LogWarning(
                "Loot table {TableId} entry {Sequence} names item template {ItemTemplateId}, which does not exist; skipping it",
                table.Id.Value, entry.Sequence, itemId.Value);
            return;
        }

        long count = random.NextInt64(entry.MinCount, (long)entry.MaxCount + 1);
        uint stack = Math.Max(1u, item.MaxStackSize);

        while (count > 0)
        {
            uint take = (uint)Math.Min(count, stack);
            drops.Add(RolledDrop.Item(item.Id, take));
            count -= take;
        }
    }

    /// <summary>
    /// The first item template of that id. The world's templates carry a lookup by id (<see cref="KeyedRows{TKey,TRow}" />,
    /// built with them and replaced with them on a reload, #883), so a drop finds its template without walking them;
    /// any other collection is walked, plainly: a lambda on the id allocated its closure and delegate per drop (#880).
    /// </summary>
    private static ItemTemplate? FindItem(IReadOnlyCollection<ItemTemplate> items, ItemTemplateId itemId)
    {
        if (items is KeyedRows<ulong, ItemTemplate> keyed)
            return keyed.Find(itemId.Value);

        if (items is IReadOnlyList<ItemTemplate> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Id == itemId)
                    return list[i];
            }

            return null;
        }

        foreach (ItemTemplate template in items)
        {
            if (template.Id == itemId)
                return template;
        }

        return null;
    }

    private ulong? RollGold(int minGold, int maxGold)
    {
        long min = Math.Max(0, minGold);
        long max = Math.Max(min, maxGold);
        if (max == 0)
            return null;

        long copper = random.NextInt64(min, max + 1);
        return copper > 0 ? (ulong)copper : null;
    }
}
