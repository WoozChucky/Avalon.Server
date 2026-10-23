using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Database.World;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;
using Avalon.Server.World.UnitTests.Handlers;
using Avalon.Server.World.UnitTests.Inventory;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Server.World.UnitTests.Characters;

/// <summary>
/// One calculation from class, level and worn gear (spec #463, #434). The golden numbers are
/// computed from the rows the World database actually seeds, so a seed change that moves them
/// fails here.
/// </summary>
public class CharacterStatsCalculatorShould
{
    private static ClassLevelStat SeededRow(CharacterClass @class, ushort level)
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        return context.ClassLevelStats.AsNoTracking().ToList().Single(s => s.Class == @class && s.Level == level);
    }

    private static DerivedCharacterStats Calc(ClassLevelStat row, ItemTemplate[] worn)
        => CharacterStatsCalculator.Calculate(row, Worn.Of(worn), TestCombat.FactorsOf(row.Class), TestCombat.Formula);

    [Theory]
    [InlineData(CharacterClass.Warrior, 1, 240u, 100u)]
    [InlineData(CharacterClass.Warrior, 5, 400u, 100u)]
    [InlineData(CharacterClass.Wizard, 1, 121u, 365u)]
    [InlineData(CharacterClass.Wizard, 5, 205u, 565u)]
    [InlineData(CharacterClass.Hunter, 1, 178u, 68u)]
    [InlineData(CharacterClass.Hunter, 5, 282u, 114u)]
    [InlineData(CharacterClass.Cleric, 1, 158u, 296u)]
    [InlineData(CharacterClass.Cleric, 5, 258u, 472u)]
    [InlineData(CharacterClass.Warrior, 10, 600u, 100u)]
    [InlineData(CharacterClass.Hunter, 10, 412u, 172u)]
    public void Derive_the_seeded_numbers_for_each_class_with_no_gear(
        CharacterClass @class, ushort level, uint health, uint power)
    {
        ClassLevelStat row = SeededRow(@class, level);

        DerivedCharacterStats stats = Calc(row, []);

        Assert.Equal(health, stats.MaxHealth);
        Assert.Equal(power, stats.MaxPower);
        Assert.Equal((row.Stamina, row.Strength, row.Agility, row.Intellect),
            (stats.Stamina, stats.Strength, stats.Agility, stats.Intellect));
    }

    /// <summary>
    /// The one attribute whose seeded growth is not a whole step per level: Warrior Agility rises
    /// by 1 and 2 in turn (20, 21, 23, 24, 26), i.e. 20 + floor(3 x (level - 1) / 2). The
    /// level-10 row pins that the levels 6-16 seed carries the same rule on.
    /// </summary>
    [Fact]
    public void Seed_a_level_10_warrior_row_that_continues_the_level_1_to_5_growth()
    {
        ClassLevelStat row = SeededRow(CharacterClass.Warrior, 10);

        Assert.Equal((400u, 0u, 40u, 41u, 33u, 20u),
            (row.BaseHp, row.BaseMana, row.Stamina, row.Strength, row.Agility, row.Intellect));
    }

    /// <summary>
    /// #1178: attributes are class-neutral. The same row gives every class the same health (BaseHp plus the formula's
    /// life per Stamina), crit (the formula's base) and defence; only power still reads the class. A flat
    /// attack speed aura passed to the calculation reaches the offence block once, on top of Agility's.
    /// </summary>
    [Theory]
    [InlineData(CharacterClass.Warrior)]
    [InlineData(CharacterClass.Wizard)]
    [InlineData(CharacterClass.Hunter)]
    [InlineData(CharacterClass.Cleric)]
    public void Turn_the_same_attributes_into_the_same_stats_for_every_class(CharacterClass @class)
    {
        var row = new ClassLevelStat
        {
            Class = @class,
            Level = 1,
            BaseHp = 100,
            Stamina = 20,
            Strength = 30,
            Agility = 40,
            Intellect = 50,
        };
        var hasted = new AuraStatModifier { Stat = AuraStat.AttackSpeed, Kind = AuraModifierKind.Flat, Value = 10f };
        var auras = AuraStatTotals.Of([hasted], static m => ((IReadOnlyList<AuraStatModifier>)[m], 1u));

        DerivedCharacterStats stats = CharacterStatsCalculator.Calculate(row, [], TestCombat.FactorsOf(@class),
            TestCombat.Formula, auras);

        Assert.Equal((200u, 5f), (stats.MaxHealth, stats.CritPct));
        Assert.Equal((3u, 45u, 12u), (stats.Armor, stats.Evasion, stats.MaxWard));   // 30 x 0.1, 40 x 1.125, 50 x 0.25
        Assert.Equal(4f + 10f, stats.Offence.AttackSpeedPct);
    }

    /// <summary>
    /// #1178: defence is class-neutral. Armour, evasion and ward are gear plus Strength, Agility and Intellect times the
    /// combat formula's conversions, each product truncated once; block and resistances come from gear alone. A level-1
    /// Warrior is Strength 23, Agility 20, Intellect 20.
    /// </summary>
    [Theory]
    [InlineData(false, 2u, 2u, 5u, 0f)]
    [InlineData(true, 15u, 9u, 9u, 10f)]
    public void Turn_attributes_and_gear_into_armour_evasion_ward_and_resistances(
        bool geared, uint armor, uint evasion, uint ward, float fire)
    {
        var formula = new CombatFormula { ArmorPerStrength = 0.1f, EvasionPerAgility = 0.1f, WardPerIntellect = 0.25f };
        var robe = new ItemTemplate
        {
            Id = new ItemTemplateId(703),
            Name = "Robe",
            Slot = ItemSlotType.Chest,
            StatType1 = StatType.Armor,
            StatValue1 = 13,
            StatType2 = StatType.Evasion,
            StatValue2 = 7,
            StatType3 = StatType.Ward,
            StatValue3 = 4,
            StatType4 = StatType.FireResistance,
            StatValue4 = 10,
        };

        DerivedCharacterStats stats = CharacterStatsCalculator.Calculate(SeededRow(CharacterClass.Warrior, 1),
            geared ? Worn.Of(robe) : [], TestCombat.FactorsOf(CharacterClass.Warrior), formula);

        Assert.Equal((armor, evasion, ward, 0f), (stats.Armor, stats.Evasion, stats.MaxWard, stats.BlockPct));
        Assert.Equal(new Resistances(fire, 0f, 0f, 0f), stats.Resistances);
    }

    [Fact]
    public void Add_worn_attributes_before_deriving_health_and_damage()
    {
        // Warrior 1: BaseHp 130, Stamina 22, Strength 23. The chestguard adds Str 2, Armor 8, Sta 2.
        DerivedCharacterStats stats =
            Calc(SeededRow(CharacterClass.Warrior, 1), [EquipTemplates.Chestguard]);

        Assert.Equal(24u, stats.Stamina);
        Assert.Equal(25u, stats.Strength);
        Assert.Equal(130u + 24u * 5u, stats.MaxHealth);
        Assert.Equal(25f * 3.5f, stats.Offence.Increased(OffenceStat.PhysicalAttack));
        Assert.Equal(8u + 2u, stats.Armor);   // and Strength 25 x 0.1
    }

    [Fact]
    public void Add_flat_health_damage_and_crit_but_never_change_a_warriors_power()
    {
        DerivedCharacterStats warrior = Calc(
            SeededRow(CharacterClass.Warrior, 1), [EquipTemplates.Chestguard, EquipTemplates.VigorAmulet]);

        Assert.Equal(130u + 24u * 5u + 15u, warrior.MaxHealth);
        Assert.Equal(100u, warrior.MaxPower);
        Assert.Equal(3f, warrior.Offence.Increased(OffenceStat.Fire));
        Assert.Equal(7.0f, warrior.CritPct);
    }

    [Fact]
    public void Add_flat_power_for_a_class_whose_pool_grows()
    {
        DerivedCharacterStats wizard =
            Calc(SeededRow(CharacterClass.Wizard, 1), [EquipTemplates.VigorAmulet]);

        Assert.Equal(121u + 15u, wizard.MaxHealth);
        Assert.Equal(365u + 20u, wizard.MaxPower);
        Assert.Equal(3f, wizard.Offence.Increased(OffenceStat.Fire));
        Assert.Equal(5f + 2f, wizard.CritPct);
    }

    /// <summary>
    /// #627, #1178: AttackSpeed and MovementSpeed are summed over the worn gear as percentages, raw (the caps are the combat
    /// formula's, applied by the entity), attack speed into the offence block on top of Agility's, and change nothing else.
    /// </summary>
    [Fact]
    public void Sum_attack_speed_and_movement_speed_over_the_worn_gear_and_change_nothing_else()
    {
        var boots = new ItemTemplate
        {
            Id = new ItemTemplateId(700),
            Name = "Boots",
            Slot = ItemSlotType.Feet,
            StatType1 = StatType.MovementSpeed,
            StatValue1 = 50,
            StatType2 = StatType.AttackSpeed,
            StatValue2 = 9,
        };
        var sword = new ItemTemplate
        {
            Id = new ItemTemplateId(702),
            Name = "Sword",
            Slot = ItemSlotType.MainHand,
            StatType1 = StatType.AttackSpeed,
            StatValue1 = 3,
            StatType2 = StatType.MovementSpeed,
            StatValue2 = 10,
        };
        ClassLevelStat row = SeededRow(CharacterClass.Warrior, 1);

        DerivedCharacterStats worn = Calc(row, [boots, sword]);

        DerivedCharacterStats bare = Calc(row, []);
        Assert.Equal((2f + 12f, 60f), (worn.Offence.AttackSpeedPct, worn.MovementSpeedPct));   // Agility 20 x 0.1, plus gear
        Assert.Equal(bare, worn with { MovementSpeedPct = 0f, Offence = bare.Offence });
    }

    [Fact]
    public void Read_all_ten_stat_pairs_and_skip_a_half_filled_one()
    {
        var trinket = new ItemTemplate
        {
            Id = new ItemTemplateId(701),
            Name = "Trinket",
            Slot = ItemSlotType.Neck,
            StatType1 = StatType.Armor,
            StatValue1 = 1,
            StatType10 = StatType.Armor,
            StatValue10 = 2,
            StatType5 = StatType.Armor,
            StatValue5 = null,
        };

        Assert.Equal(3u + 2u, Calc(SeededRow(CharacterClass.Hunter, 1), [trinket]).Armor);   // and Strength 21 x 0.1
    }

    /// <summary>
    /// Review Focus 4 (#1179, A-R13, A-R14): a local line counts only on the item it sits on. The head's 50 % increased
    /// armour scales its own 5 + 3 to 12, never the chest's 8, and the gloves' 100 % scales no armour of theirs; the main
    /// hand's added damage, 40 % increased physical damage and 10 % attack speed are its own (4-7 to 6-10, added physical
    /// 1-2 to 1-3, 792 ms to 720), never the offence block's; the ring's added damage to attacks and to spells is global.
    /// </summary>
    [Theory]
    [InlineData("armor", 20d)]
    [InlineData("weapon min1", 6d)]
    [InlineData("weapon max1", 10d)]
    [InlineData("weapon added physical min", 1d)]
    [InlineData("weapon added physical max", 3d)]
    [InlineData("weapon added fire max", 5d)]
    [InlineData("attack time", 720d)]
    [InlineData("attacks physical max", 3d)]
    [InlineData("spells cold max", 2d)]
    [InlineData("global increased physical", 0d)]
    public void Keep_local_modifiers_on_the_item_they_sit_on(string what, double expected)
    {
        var formula = new CombatFormula { ArmorPerStrength = 0f };
        var head = new ItemTemplate { Name = "Head", Slot = ItemSlotType.Head, StatType1 = StatType.Armor, StatValue1 = 5 };
        var chest = new ItemTemplate { Name = "Chest", Slot = ItemSlotType.Chest, StatType1 = StatType.Armor, StatValue1 = 8 };
        var mainHand = new ItemTemplate
        {
            Name = "Sword",
            Slot = ItemSlotType.MainHand,
            DamageMin1 = 4,
            DamageMax1 = 7,
            DamageType1 = DamageType.Physical,
            AttackTimeMs = 792,
        };
        var ring = new ItemTemplate { Name = "Ring", Slot = ItemSlotType.Finger };
        var gloves = new ItemTemplate { Name = "Gloves", Slot = ItemSlotType.Hands };
        WornItem[] worn =
        [
            Rolled(head, (StatType.Armor, 3, null), (StatType.LocalIncreasedArmor, 50, null)),
            Rolled(chest),
            Rolled(mainHand, (StatType.AddedPhysicalDamage, 1, 2), (StatType.LocalIncreasedPhysicalDamage, 40, null),
                (StatType.AddedFireDamage, 2, 5), (StatType.LocalAttackSpeed, 10, null)),
            Rolled(ring, (StatType.AddedPhysicalDamageToAttacks, 1, 3), (StatType.AddedColdDamageToSpells, 1, 2)),
            Rolled(gloves, (StatType.LocalIncreasedArmor, 100, null)),
        ];

        DerivedCharacterStats stats = CharacterStatsCalculator.Calculate(SeededRow(CharacterClass.Warrior, 1), worn,
            TestCombat.FactorsOf(CharacterClass.Warrior), formula);

        double actual = what switch
        {
            "armor" => stats.Armor,
            "weapon min1" => stats.Weapon.Min1,
            "weapon max1" => stats.Weapon.Max1,
            "weapon added physical min" => stats.Weapon.Added.Min(DamageType.Physical),
            "weapon added physical max" => stats.Weapon.Added.Max(DamageType.Physical),
            "weapon added fire max" => stats.Weapon.Added.Max(DamageType.Fire),
            "attack time" => stats.Weapon.AttackTimeMs,
            "attacks physical max" => stats.Offence.AddedToAttacks.Max(DamageType.Physical),
            "spells cold max" => stats.Offence.AddedToSpells.Max(DamageType.Cold),
            _ => stats.Offence.Increased(OffenceStat.Physical),
        };
        Assert.Equal(expected, actual);
    }

    /// <summary>A copy of <paramref name="template" /> whose modifiers hold <paramref name="values" />, two to a modifier.</summary>
    private static WornItem Rolled(ItemTemplate template, params (StatType Stat, int Value, int? Value2)[] values)
    {
        var item = new ItemInstanceId(Guid.CreateVersion7());
        return new WornItem(template, [.. values.Chunk(2).Select((pair, ordinal) => TestModifiers.Modifier(item, 1,
            (byte)ordinal, [.. pair.Select((v, i) => ((byte)i, v.Stat, v.Value, v.Value2))]))]);
    }

    [Fact]
    public void Write_every_value_to_the_character_stats_row()
    {
        DerivedCharacterStats stats = new(MaxHealth: 260, MaxPower: 100, Stamina: 24, Strength: 25, Agility: 20,
            Intellect: 20, Armor: 8, BlockPct: 5f, Evasion: 4, CritPct: 5f, MaxWard: 6,
            Resistances: new Resistances(11f, 12f, 13f, 14f));

        CharacterStats row = stats.ToRow(new CharacterId(7));

        Assert.Equal(new CharacterId(7), row.CharacterId);
        Assert.Equal((260u, 100u, 0u), (row.MaxHealth, row.MaxPower1, row.MaxPower2));
        Assert.Equal((24u, 25u, 20u, 20u), (row.Stamina, row.Strength, row.Agility, row.Intellect));
        Assert.Equal((8u, 5f, 5f), (row.Armor, row.BlockPct, row.CritPct));
        Assert.Equal((4u, 6u, 11f, 12f, 13f, 14f), (row.Evasion, row.MaxWard, row.FireResistance, row.ColdResistance,
            row.LightningResistance, row.BlightResistance));
    }

    /// <summary>Review Focus 3: a gear change never kills, revives or overfills.</summary>
    [Theory]
    [InlineData(120u, 240u, 260u, 130u)]  // half stays half
    [InlineData(240u, 240u, 260u, 260u)]  // full stays full
    [InlineData(300u, 240u, 260u, 260u)]  // above the old maximum counts as full
    [InlineData(0u, 240u, 260u, 0u)]      // empty stays empty
    [InlineData(1u, 240u, 10u, 1u)]       // anything left keeps at least 1
    [InlineData(99u, 100u, 1000u, 990u)]  // rounds to the nearest point
    [InlineData(50u, 0u, 100u, 100u)]     // a pool that had no maximum fills
    [InlineData(50u, 100u, 0u, 0u)]       // a maximum of 0 holds nothing, and does not throw
    public void Keep_the_share_of_a_pool_across_a_new_maximum(uint current, uint oldMax, uint newMax, uint expected)
    {
        Assert.Equal(expected, CharacterStatsCalculator.KeepShare(current, oldMax, newMax));
    }
}
