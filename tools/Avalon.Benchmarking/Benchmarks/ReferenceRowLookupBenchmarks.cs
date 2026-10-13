using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Reload;
using BenchmarkDotNet.Attributes;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// What a kill looks up in the reference data (#883): the item template of each drop (<c>LootRoller.FindItem</c>) and
/// the experience requirement of a level (<c>ExperienceAward.Requirement</c>, asked twice for each member the kill's
/// experience is split among and twice more for each award). One invocation looks every row up once, so it is
/// <see cref="Rows" /> lookups, one at each position of the list. The <c>Walk</c> cases are the lookups as they were,
/// a walk of the rows comparing keys; the <c>Keyed</c> cases are <see cref="KeyedRows{TKey,TRow}" />, which the Items
/// and Progression areas now publish their rows as. The seed holds 31 item templates.
/// </summary>
[MemoryDiagnoser]
public class ReferenceRowLookupBenchmarks
{
    private IReadOnlyCollection<ItemTemplate> _items = null!;
    private IReadOnlyCollection<ItemTemplate> _keyedItems = null!;
    private ItemTemplateId[] _itemIds = null!;
    private IReadOnlyCollection<CharacterLevelExperience> _levels = null!;
    private IReadOnlyCollection<CharacterLevelExperience> _keyedLevels = null!;

    [Params(8, 32, 128)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // As the repositories hand them over: a read-only view of a list.
        _items = Enumerable.Range(1, Rows)
            .Select(id => new ItemTemplate { Id = new ItemTemplateId((ulong)id), Name = "Bench" }).ToList().AsReadOnly();
        _keyedItems = KeyedRows<ulong, ItemTemplate>.Over(_items, static template => template.Id?.Value);
        _itemIds = _items.Select(template => new ItemTemplateId(template.Id.Value)).ToArray();

        _levels = Enumerable.Range(1, Rows)
            .Select(level => new CharacterLevelExperience { Level = (ushort)level, Experience = 100 }).ToList().AsReadOnly();
        _keyedLevels = KeyedRows<ushort, CharacterLevelExperience>.Over(_levels, static row => row.Level);

        if (Walk_item_templates() != Rows || Keyed_item_templates() != Rows || Walk_level_rows() != Rows
            || Keyed_level_rows() != Rows)
        {
            throw new InvalidOperationException("Not every row was found.");
        }
    }

    [Benchmark(Baseline = true)]
    public int Walk_item_templates() => FindItems(_items);

    [Benchmark]
    public int Keyed_item_templates() => FindItems(_keyedItems);

    [Benchmark]
    public int Walk_level_rows() => FindLevels(_levels);

    [Benchmark]
    public int Keyed_level_rows() => FindLevels(_keyedLevels);

    private int FindItems(IReadOnlyCollection<ItemTemplate> items)
    {
        int found = 0;
        foreach (ItemTemplateId id in _itemIds)
        {
            if (FindItem(items, id) is not null)
                found++;
        }

        return found;
    }

    private int FindLevels(IReadOnlyCollection<CharacterLevelExperience> rows)
    {
        int found = 0;
        for (int level = 1; level <= Rows; level++)
        {
            if (Requirement(rows, (ushort)level) is not null)
                found++;
        }

        return found;
    }

    // LootRoller.FindItem, as it is.
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
        }

        return null;
    }

    // ExperienceAward.Requirement, as it is.
    private static CharacterLevelExperience? Requirement(IReadOnlyCollection<CharacterLevelExperience> rows, ushort level)
    {
        if (rows is KeyedRows<ushort, CharacterLevelExperience> keyed)
            return keyed.Find(level);

        if (rows is IReadOnlyList<CharacterLevelExperience> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Level == level)
                    return list[i];
            }
        }

        return null;
    }
}
