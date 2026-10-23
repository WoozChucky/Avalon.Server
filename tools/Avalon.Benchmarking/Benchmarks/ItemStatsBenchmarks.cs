using Avalon.Combat;
using Avalon.Common;
using Avalon.Database.World.Seeding;
using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;
using BenchmarkDotNet.Attributes;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// The stats calculation a stats refresh runs (#1179): <see cref="CharacterStatsCalculator.Calculate" /> for a level-10
/// Warrior (the seeded row: BaseHp 400, Stamina 40, Strength 41, Agility 33, Intellect 20) on the seeded formula and the
/// Warrior's factors, wearing a full forest kit: the five Barkplate pieces (items 12-16), the Bramblesteel Sword (7), the
/// Alpha's Fang Pendant (58), the Heartwood Band (64) and the Thornheart Signet (65). The world passes the worn templates
/// as a list it reuses, so the benchmark does too; no auras. 0 B expected.
/// </summary>
/// <remarks>
/// The templates are literals of the seed rows as they stood before #1179 part A replaced them with bases (their old
/// rarities and item power have no place in the new vocabulary and play no part in the stats), so the benchmark keeps
/// measuring the same kit now those rows are gone. The gear gives 16 Stamina, so the maximum health is
/// 400 + 56 x 5 = 680; setup fails the run if it differs.
/// </remarks>
[MemoryDiagnoser]
public class ItemStatsBenchmarks
{
    private const uint ExpectedMaxHealth = 680;

    private static readonly ClassLevelStat s_warriorLevel10 = new()
    {
        Class = CharacterClass.Warrior,
        Level = 10,
        BaseHp = 400,
        BaseMana = 0,
        Stamina = 40,
        Strength = 41,
        Agility = 33,
        Intellect = 20,
    };

    private readonly List<ItemTemplate> _worn =
    [
        ForestArmourPiece(12, "Barkplate Helm", ItemSubClass.Helmet, ItemSlotType.Head, strength: 1, armor: 4, stamina: 1),
        ForestArmourPiece(13, "Barkplate Chestguard", ItemSubClass.Chest, ItemSlotType.Chest, strength: 2, armor: 8, stamina: 2),
        ForestArmourPiece(14, "Barkplate Legguards", ItemSubClass.Legs, ItemSlotType.Legs, strength: 2, armor: 6, stamina: 1),
        ForestArmourPiece(15, "Barkplate Gauntlets", ItemSubClass.Gloves, ItemSlotType.Hands, strength: 1, armor: 3, stamina: 1),
        ForestArmourPiece(16, "Barkplate Boots", ItemSubClass.Boots, ItemSlotType.Feet, strength: 1, armor: 3, stamina: 1),
        new()
        {
            Id = 7,
            Name = "Bramblesteel Sword",
            Class = ItemClass.Weapon,
            SubClass = ItemSubClass.OneHanded,
            MaxStackSize = 1,
            DisplayId = 7,
            Rarity = ItemRarity.Common,
            BuyPrice = 200,
            SellPrice = 50,
            Slot = ItemSlotType.MainHand,
            AllowedClasses = [CharacterClass.Warrior],
            RequiredLevel = 1,
            DamageMin1 = 5,
            DamageMax1 = 7,
            DamageType1 = DamageType.Physical,
            AttackTimeMs = 792,
            StatType1 = StatType.Strength,
            StatValue1 = 1,
        },
        Jewellery(58, "Alpha's Fang Pendant", ItemSubClass.Amulet, ItemSlotType.Neck, 200, 50, 2,
            stamina: 2, armor: 1),
        Jewellery(64, "Heartwood Band", ItemSubClass.Ring, ItemSlotType.Finger, 400, 100, 5,
            stamina: 3, armor: 2),
        Jewellery(65, "Thornheart Signet", ItemSubClass.Ring, ItemSlotType.Finger, 800, 200, 7,
            stamina: 5, armor: 4),
    ];

    private readonly ClassStatFactors _factors = CombatSeed.ClassFactors()[0];
    private readonly CombatFormula _formula = CombatSeed.Formula();

    [GlobalSetup]
    public void Setup()
    {
        if (_factors.Class != CharacterClass.Warrior)
            throw new InvalidOperationException($"The first seeded factors row is {_factors.Class}, not the Warrior's.");

        uint maxHealth = Calculate().MaxHealth;
        if (maxHealth != ExpectedMaxHealth)
            throw new InvalidOperationException($"The maximum health is {maxHealth}, not {ExpectedMaxHealth}.");
    }

    [Benchmark]
    public DerivedCharacterStats Calculate() =>
        CharacterStatsCalculator.Calculate(s_warriorLevel10, _worn, _factors, _formula);

    private static ItemTemplate ForestArmourPiece(ulong id, string name, ItemSubClass subClass, ItemSlotType slot,
        uint strength, uint armor, uint stamina) => new()
        {
            Id = id,
            Name = name,
            Class = ItemClass.Armor,
            SubClass = subClass,
            MaxStackSize = 1,
            DisplayId = (uint)id,
            Rarity = ItemRarity.Common,
            BuyPrice = 200,
            SellPrice = 50,
            Slot = slot,
            AllowedClasses = [CharacterClass.Warrior],
            RequiredLevel = 1,
            StatType1 = StatType.Strength,
            StatValue1 = strength,
            StatType2 = StatType.Armor,
            StatValue2 = armor,
            StatType3 = StatType.Stamina,
            StatValue3 = stamina,
        };

    private static ItemTemplate Jewellery(ulong id, string name, ItemSubClass subClass, ItemSlotType slot,
        uint buyPrice, uint sellPrice, ushort requiredLevel, uint stamina,
        uint armor) => new()
        {
            Id = id,
            Name = name,
            Class = ItemClass.Armor,
            SubClass = subClass,
            MaxStackSize = 1,
            DisplayId = (uint)id,
            Rarity = ItemRarity.Common,
            BuyPrice = buyPrice,
            SellPrice = sellPrice,
            Slot = slot,
            RequiredLevel = requiredLevel,
            StatType1 = StatType.Stamina,
            StatValue1 = stamina,
            StatType2 = StatType.Armor,
            StatValue2 = armor,
        };
}
