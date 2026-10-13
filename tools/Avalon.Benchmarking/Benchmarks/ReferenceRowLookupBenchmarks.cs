using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Reload;
using BenchmarkDotNet.Attributes;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// What a kill looks up in the reference data (#883): the item template of each drop and the experience requirement
/// of a level (asked twice for each member the kill's experience is split among and twice more for each award). One
/// invocation looks every row up once, so it is <see cref="Rows" /> lookups, one at each position of the list.
/// </summary>
/// <remarks>
/// The <c>Walk</c> cases are the lookups as they were, over the collections production held: the item templates a
/// read-only view of the repository's list (<c>LootRoller.FindItem</c>, which still walks a collection that carries no
/// lookup), the level rows the repository's list itself (<c>ExperienceAward.Requirement</c>, whose walk is gone and is
/// copied here). The <c>Keyed</c> cases are the lookups production runs now, <see cref="ItemsPatch.Find" /> and
/// <see cref="ProgressionPatch.FindLevel" />. The seed holds 31 item templates and 15 level rows.
/// </remarks>
[MemoryDiagnoser]
public class ReferenceRowLookupBenchmarks
{
    private IReadOnlyCollection<ItemTemplate> _items = null!;
    private ItemsPatch _itemsPatch = null!;
    private ItemTemplateId[] _itemIds = null!;
    private IReadOnlyCollection<CharacterLevelExperience> _levels = null!;
    private ProgressionPatch _progressionPatch = null!;

    [Params(8, 15, 32, 128)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // As StaticData held them: FindAllAsync(...).AsReadOnly() for the items, the list GetAllAsync returns for the levels.
        _items = Enumerable.Range(1, Rows)
            .Select(id => new ItemTemplate { Id = new ItemTemplateId((ulong)id), Name = "Bench" }).ToList().AsReadOnly();
        _itemsPatch = new ItemsPatch(_items);
        _itemIds = _items.Select(template => new ItemTemplateId(template.Id.Value)).ToArray();

        _levels = Enumerable.Range(1, Rows)
            .Select(level => new CharacterLevelExperience { Level = (ushort)level, Experience = 100 }).ToList();
        _progressionPatch = new ProgressionPatch(_levels, [], []);

        if (Walk_item_templates() != Rows || Keyed_item_templates() != Rows || Walk_level_rows() != Rows
            || Keyed_level_rows() != Rows)
        {
            throw new InvalidOperationException("Not every row was found.");
        }
    }

    [Benchmark(Baseline = true)]
    public int Walk_item_templates()
    {
        int found = 0;
        foreach (ItemTemplateId id in _itemIds)
        {
            if (WalkItems(_items, id) is not null)
                found++;
        }

        return found;
    }

    [Benchmark]
    public int Keyed_item_templates()
    {
        int found = 0;
        foreach (ItemTemplateId id in _itemIds)
        {
            if (_itemsPatch.Find(id) is not null)
                found++;
        }

        return found;
    }

    [Benchmark]
    public int Walk_level_rows()
    {
        int found = 0;
        for (int level = 1; level <= Rows; level++)
        {
            if (WalkLevels(_levels, (ushort)level) is not null)
                found++;
        }

        return found;
    }

    [Benchmark]
    public int Keyed_level_rows()
    {
        int found = 0;
        for (int level = 1; level <= Rows; level++)
        {
            if (_progressionPatch.FindLevel((ushort)level) is not null)
                found++;
        }

        return found;
    }

    // LootRoller.FindItem's walk, as it was for the world's templates and still is for a collection with no lookup.
    private static ItemTemplate? WalkItems(IReadOnlyCollection<ItemTemplate> items, ItemTemplateId itemId)
    {
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

    // ExperienceAward.Requirement's walk, as it was.
    private static CharacterLevelExperience? WalkLevels(IReadOnlyCollection<CharacterLevelExperience> rows, ushort level)
    {
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
