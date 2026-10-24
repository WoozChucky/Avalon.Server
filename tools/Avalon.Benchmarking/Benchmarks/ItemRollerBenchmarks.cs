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
/// Making one copy of an item with its base stats and modifiers (#1179), once per drop, purchase, reward or starting item,
/// never per tick or per hit, over the seeded templates and modifier catalog. Both make a plate chest at item level 15,
/// the Ironroot Chestguard (86, the line's best tier there). <see cref="RollPlain" /> is a common copy as a vendor
/// purchase or an item script makes it: its armour, then its one implicit (Armor). <see cref="RollRare" /> is a drop's
/// full roll, forced Rare, in the roll's draw order: the rarity draw, the base tier, the base stats (its armour), the
/// implicit's value, the affix count, each affix's weighted pick among the chest's candidates and its values, and the
/// two-word name. The draws come from a fixed sequence reset before each roll, so every operation makes the same item;
/// setup picks the first seed whose rare carries its implicit and six affixes, so a change to the draw order moves the
/// seed rather than the measured item, and fails the run when none of the first seeds does. Expected allocations: the
/// copy (<see cref="RolledItem" />, its base stats inside it by value, no object of their own), its modifier rows, each
/// row's value list and value, and the copy's modifier list; the rare's name besides.
/// </summary>
[MemoryDiagnoser]
public class ItemRollerBenchmarks
{
    private const int ExpectedRareModifiers = 7;

    // How many seeds setup tries for a rare of ExpectedRareModifiers; the affix count alone (3-6) lands on six about one
    // seed in four.
    private const int SeedsTried = 1000;

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
        if (plain.Modifiers.Count != 1 || plain.BaseStats.Armour is null)
        {
            throw new InvalidOperationException(
                $"The plain chest rolled {plain.Modifiers.Count} modifiers and armour {plain.BaseStats.Armour}, " +
                "not its armour and its one implicit.");
        }

        for (ulong seed = 1; seed <= SeedsTried; seed++)
        {
            _random.Seed = seed;
            if (IsTheMeasuredRare(RollRare()))
                return;
        }

        throw new InvalidOperationException(
            $"None of the first {SeedsTried} seeds rolls a named Rare {s_plateChest.Value} with its armour and " +
            $"{ExpectedRareModifiers} modifiers.");
    }

    [Benchmark]
    public RolledItem RollPlain()
    {
        _random.Reset();
        return _roller.RollPlain(_chest, 15, _items);
    }

    [Benchmark]
    public RolledItem RollRare()
    {
        // The rarity draw's 0.99 is Rare by the Normal odds (Rare above 97).
        _random.Reset(firstDouble: 0.99);
        return _roller.Roll(_chest, new DropContext(15, CreatureRarity.Normal, ForceMagic: false), _items)!;
    }

    private static bool IsTheMeasuredRare(RolledItem rare) =>
        rare.Rarity == ItemRarity.Rare && rare.TemplateId.Value == s_plateChest.Value && rare.BaseStats.Armour is not null
        && rare.Modifiers.Count == ExpectedRareModifiers && rare.RareName is not null;

    /// <summary>
    /// A fixed sequence of draws (SplitMix64 from <see cref="Seed" />), restarted by <see cref="Reset" />, so each roll
    /// draws the same numbers; the first <see cref="NextDouble" /> after a reset may be pinned. Allocates nothing.
    /// </summary>
    private sealed class ScriptedRandom : ILootRandom
    {
        private ulong _state = 1;
        private double? _firstDouble;

        /// <summary>The sequence's seed, chosen by setup; each reset starts from it.</summary>
        public ulong Seed { get; set; } = 1;

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
