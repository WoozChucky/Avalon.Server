using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Loot;

/// <summary>
/// Rolls what a dying creature drops. Tick thread. It allocates the result list, and every entry
/// that hits allocates again, because its item template is looked up with a LINQ scan (a closure
/// and an enumerator) over the templates.
/// </summary>
public interface ILootRoller
{
    /// <summary>
    /// Item stacks in roll order, then at most one gold pile, always last. Reads
    /// <paramref name="template"/>'s table from <paramref name="catalog"/> as it is now, so a Loot
    /// reload applies to the next kill.
    /// </summary>
    IReadOnlyList<RolledDrop> Roll(CreatureTemplate template, LootCatalog catalog, IReadOnlyCollection<ItemTemplate> items);
}

public sealed class LootRoller(ILootRandom random, ILogger<LootRoller> logger) : ILootRoller
{
    /// <summary>
    /// How many references deep a roll may go; the creature's own table is depth 0. The catalog
    /// refuses cycles, so this is the backstop if one ever got through.
    /// </summary>
    public const int MaxReferenceDepth = 8;

    public IReadOnlyList<RolledDrop> Roll(CreatureTemplate template, LootCatalog catalog, IReadOnlyCollection<ItemTemplate> items)
    {
        var drops = new List<RolledDrop>();

        if (template.LootTableId is { } tableId)
        {
            if (catalog.TryGet(tableId, out LootTableView? table))
            {
                RollTable(table, 0, catalog, items, drops);
            }
            else
            {
                logger.LogWarning(
                    "Creature template {TemplateId} names loot table {TableId}, which is missing or was refused; it drops only gold",
                    template.Id.Value, tableId.Value);
            }
        }

        if (RollGold(template) is { } copper)
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

        foreach (LootEntryView entry in table.Ungrouped)
        {
            if (random.NextDouble() * 100.0 < entry.Chance)
                Resolve(table, entry, depth, catalog, items, drops);
        }

        foreach (LootGroupView group in table.Groups)
        {
            if (PickByWeight(group) is { } picked)
                Resolve(table, picked, depth, catalog, items, drops);
        }
    }

    /// <summary>Exactly one entry, weighted by Chance; null only when the weights add up to 0.</summary>
    private LootEntryView? PickByWeight(LootGroupView group)
    {
        double total = 0;
        foreach (LootEntryView entry in group.Entries)
            total += Math.Max(0f, entry.Chance);

        if (total <= 0)
            return null;

        double pick = random.NextDouble() * total;
        double cumulative = 0;
        LootEntryView? last = null;

        foreach (LootEntryView entry in group.Entries)
        {
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
        ItemTemplate? item = items.FirstOrDefault(t => t.Id == itemId);
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

    private ulong? RollGold(CreatureTemplate template)
    {
        long min = Math.Max(0, template.MinGold);
        long max = Math.Max(min, template.MaxGold);
        if (max == 0)
            return null;

        long copper = random.NextInt64(min, max + 1);
        return copper > 0 ? (ulong)copper : null;
    }
}
