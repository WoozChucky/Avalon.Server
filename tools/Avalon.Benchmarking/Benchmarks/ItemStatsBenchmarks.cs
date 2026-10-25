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
/// Alpha's Fang Pendant (58), the Heartwood Band (64) and the Thornheart Signet (65). <see cref="Calculate" /> wears each
/// piece with no modifiers, as the kit was measured before instance modifiers; <see cref="CalculateModified" /> wears the
/// same pieces with six modifiers each, local and global lines mixed (local defence and weapon lines, added damage to
/// attacks and spells, attributes, resistances, crit). The world passes the worn items as a list it reuses, so the
/// benchmark does too; no auras. 0 B expected.
/// </summary>
/// <remarks>
/// The templates are literals of the seed rows as they stood before #1179 part A replaced them with bases (their old
/// rarities and item power have no place in the new vocabulary and play no part in the stats), so the benchmark keeps
/// measuring the same kit now those rows are gone. The gear gives 16 Stamina, so the maximum health is
/// 400 + 56 x 5 = 680; each piece's modifiers add 2 Stamina more, 770 in all. Setup fails the run if either differs.
/// </remarks>
[MemoryDiagnoser]
public class ItemStatsBenchmarks
{
    private const uint ExpectedMaxHealth = 680;
    private const uint ExpectedModifiedMaxHealth = 770;

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

    private static readonly List<ItemTemplate> s_kit =
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
            DamageType = DamageType.Physical,
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

    // Each piece as a copy that rolled no modifiers, as the kit was measured before #1179, with its base stats.
    private readonly List<WornItem> _worn = [.. s_kit.Select(t => new WornItem(t, BaseStatsFor(t), null, t.DamageType))];

    // The same pieces, each with six modifiers of one value each.
    private readonly List<WornItem> _wornModified = [.. s_kit.Select(t => new WornItem(t, BaseStatsFor(t), ModifiersFor(t), t.DamageType))];

    private readonly ClassStatFactors _factors = CombatSeed.ClassFactors().Single(f => f.Class == CharacterClass.Warrior);
    private readonly CombatFormula _formula = CombatSeed.Formula();

    [GlobalSetup]
    public void Setup()
    {
        uint maxHealth = Calculate().MaxHealth;
        if (maxHealth != ExpectedMaxHealth)
            throw new InvalidOperationException($"The maximum health is {maxHealth}, not {ExpectedMaxHealth}.");

        uint modifiedMaxHealth = CalculateModified().MaxHealth;
        if (modifiedMaxHealth != ExpectedModifiedMaxHealth)
        {
            throw new InvalidOperationException(
                $"The modified kit's maximum health is {modifiedMaxHealth}, not {ExpectedModifiedMaxHealth}.");
        }
    }

    [Benchmark]
    public DerivedCharacterStats Calculate() =>
        CharacterStatsCalculator.Calculate(s_warriorLevel10, _worn, _factors, _formula);

    [Benchmark]
    public DerivedCharacterStats CalculateModified() =>
        CharacterStatsCalculator.Calculate(s_warriorLevel10, _wornModified, _factors, _formula);

    // The sword's copy rolled the damage and attack time its template carried before base stats (5-7 at 792 ms); each
    // Barkplate piece's copy rolled the armour its template line gave, from its one-value range, as a seeded base's
    // copy carries its defence. The jewellery keeps its template lines: a ring or an amulet carries no base stats.
    private static ItemBaseStats BaseStatsFor(ItemTemplate template) => template.Slot == ItemSlotType.MainHand
        ? new ItemBaseStats(null, null, null, null, DamageMin: 5, DamageMax: 7, AttackTime: 792)
        : template.BaseStats is [{ Stat: ItemBaseStat.Armour } armour] ? new ItemBaseStats { Armour = armour.Min } : default;

    // Six modifiers by kind of piece: the sword's local damage and speed, armour's local increased armour, jewellery's
    // added damage to spells; every piece has 2 Stamina and some global lines.
    private static List<ItemInstanceModifier> ModifiersFor(ItemTemplate template) => template.Slot switch
    {
        ItemSlotType.MainHand => Modifiers(
            (StatType.Stamina, 2, null),
            (StatType.AddedPhysicalDamage, 2, 5),
            (StatType.AddedFireDamage, 1, 4),
            (StatType.LocalIncreasedPhysicalDamage, 25, null),
            (StatType.LocalAttackSpeed, 8, null),
            (StatType.IncreasedAttackDamage, 10, null)),
        ItemSlotType.Neck or ItemSlotType.Finger => Modifiers(
            (StatType.Stamina, 2, null),
            (StatType.AddedColdDamageToAttacks, 1, 2),
            (StatType.AddedLightningDamageToSpells, 1, 6),
            (StatType.ColdResistance, 5, null),
            (StatType.CastSpeed, 4, null),
            (StatType.Strength, 3, null)),
        _ => Modifiers(
            (StatType.Stamina, 2, null),
            (StatType.Armor, 5, null),
            (StatType.LocalIncreasedArmor, 20, null),
            (StatType.FireResistance, 6, null),
            (StatType.AddedPhysicalDamageToAttacks, 1, 3),
            (StatType.CritPct, 1, null)),
    };

    private static List<ItemInstanceModifier> Modifiers(params (StatType Stat, int Value, int? Value2)[] lines)
    {
        var modifiers = new List<ItemInstanceModifier>(lines.Length);
        for (int i = 0; i < lines.Length; i++)
        {
            var id = Guid.NewGuid();
            modifiers.Add(new ItemInstanceModifier
            {
                Id = id,
                ModifierId = (ushort)(i + 1),
                Ordinal = (byte)i,
                Values =
                [
                    new ItemInstanceModifierValue
                    {
                        ItemInstanceModifierId = id,
                        Index = 0,
                        Stat = lines[i].Stat,
                        Value = lines[i].Value,
                        Value2 = lines[i].Value2,
                    },
                ],
            });
        }

        return modifiers;
    }

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
            StatType2 = StatType.Stamina,
            StatValue2 = stamina,
            BaseStats = [new ItemTemplateBaseStat { TemplateId = id, Stat = ItemBaseStat.Armour, Min = (int)armor, Max = (int)armor }],
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
