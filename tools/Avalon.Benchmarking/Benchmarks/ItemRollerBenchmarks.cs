using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World;
using Avalon.World.Configuration;
using Avalon.World.Inventory;
using Avalon.World.Loot;
using Avalon.World.Public.Enums;
using Avalon.World.Reload;
using Avalon.World.Testing.Scenarios;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Options;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// Making one copy of an item with its modifiers (#1179), once per drop, purchase, reward or starting item, never per
/// tick or per hit, over the seeded templates and modifier catalog. Both make a plate chest at item level 15, the
/// Ironroot Chestguard (86, the line's best tier there). <see cref="RollPlain" /> is a common copy as a vendor purchase or
/// an item script makes it: its one implicit (Armor). <see cref="RollRare" /> is a drop's full roll, forced Rare: the
/// rarity draw, the base tier, the implicit, the affix count, each affix's weighted pick among the chest's candidates
/// and its values, and the two-word name. The draws come from a fixed sequence reset before each roll, so every
/// operation makes the same item; setup fails the run unless the rare carries its implicit and six affixes. Expected
/// allocations: the copy (<see cref="RolledItem" />), its modifier rows, each row's value list and value, and the
/// copy's modifier list; the rare's name besides.
/// </summary>
[MemoryDiagnoser]
public class ItemRollerBenchmarks
{
    private const int ExpectedRareModifiers = 7;

    private static readonly ItemTemplateId s_plateChest = new(86);

    private readonly ScriptedRandom _random = new();
    private ItemsPatch _items = null!;
    private ItemTemplate _chest = null!;
    private ItemRoller _roller = null!;

    [GlobalSetup]
    public void Setup()
    {
        StaticData data = ScenarioReferenceData.Shared.Data;
        _items = new ItemsPatch(data.ItemTemplates, data.ItemModifiers);
        _chest = _items.Find(s_plateChest)!;
        _roller = new ItemRoller(_random, new ItemIdAllocator(), Options.Create(new GameConfiguration()));

        RolledItem plain = RollPlain();
        if (plain.Modifiers.Count != 1)
            throw new InvalidOperationException($"The plain chest rolled {plain.Modifiers.Count} modifiers, not its one implicit.");

        RolledItem rare = RollRare();
        if (rare.Rarity != ItemRarity.Rare || rare.TemplateId.Value != s_plateChest.Value
                                           || rare.Modifiers.Count != ExpectedRareModifiers || rare.RareName is null)
        {
            throw new InvalidOperationException(
                $"The rare roll made a {rare.Rarity} {rare.TemplateId.Value} with {rare.Modifiers.Count} modifiers, " +
                $"not a named Rare {s_plateChest.Value} with {ExpectedRareModifiers}.");
        }
    }

    [Benchmark]
    public RolledItem RollPlain()
    {
        _random.Reset();
        return _roller.RollPlain(_chest, 15, _items.Modifiers);
    }

    [Benchmark]
    public RolledItem RollRare()
    {
        // The rarity draw's 0.99 is Rare by the Normal odds (Rare above 97).
        _random.Reset(firstDouble: 0.99);
        return _roller.Roll(_chest, new DropContext(15, CreatureRarity.Normal, ForceMagic: false), _items)!;
    }

    /// <summary>
    /// A fixed sequence of draws (SplitMix64 from one seed), restarted by <see cref="Reset" />, so each roll draws the
    /// same numbers; the first <see cref="NextDouble" /> after a reset may be pinned. Allocates nothing.
    /// </summary>
    private sealed class ScriptedRandom : ILootRandom
    {
        // A seed whose sequence gives the rare roll six affixes (3-6, drawn after the implicit's value).
        private const ulong Seed = 1;

        private ulong _state = Seed;
        private double? _firstDouble;

        public void Reset(double? firstDouble = null)
        {
            _state = Seed;
            _firstDouble = firstDouble;
        }

        public double NextDouble()
        {
            if (_firstDouble is { } pinned)
            {
                _firstDouble = null;
                return pinned;
            }

            return (Next() >> 11) * (1.0 / (1UL << 53));
        }

        public long NextInt64(long minInclusive, long maxExclusive) =>
            minInclusive + (long)(Next() % (ulong)(maxExclusive - minInclusive));

        private ulong Next()
        {
            ulong z = _state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
