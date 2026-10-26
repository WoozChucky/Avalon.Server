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
        => CharacterStatsCalculator.Calculate(row, Worn.Of(worn), TestCombat.Formula);

    [Theory]
    [InlineData(CharacterClass.Warrior, 1, 240u, 180u)]
    [InlineData(CharacterClass.Warrior, 5, 400u, 212u)]
    [InlineData(CharacterClass.Wizard, 1, 121u, 192u)]
    [InlineData(CharacterClass.Wizard, 5, 205u, 256u)]
    [InlineData(CharacterClass.Hunter, 1, 178u, 180u)]
    [InlineData(CharacterClass.Hunter, 5, 282u, 212u)]
    [InlineData(CharacterClass.Cleric, 1, 158u, 192u)]
    [InlineData(CharacterClass.Cleric, 5, 258u, 256u)]
    [InlineData(CharacterClass.Warrior, 10, 600u, 252u)]
    [InlineData(CharacterClass.Hunter, 10, 412u, 252u)]
    [InlineData(CharacterClass.Warrior, 15, 800u, 292u)]
    [InlineData(CharacterClass.Wizard, 15, 415u, 416u)]
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

        Assert.Equal((400u, 40u, 41u, 33u, 20u),
            (row.BaseHp, row.Stamina, row.Strength, row.Agility, row.Intellect));
    }

    /// <summary>
    /// #1178: attributes are class-neutral. The same row gives every class the same health (BaseHp plus the formula's
    /// life per Stamina), crit (the formula's base) and defence, and the same mana (#1181: 100 plus 50 Intellect x 4). A flat
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

        DerivedCharacterStats stats = CharacterStatsCalculator.Calculate(row, [], TestCombat.Formula, auras);

        Assert.Equal((200u, 300u, 5f), (stats.MaxHealth, stats.MaxPower, stats.CritPct));
        Assert.Equal((3u, 45u, 12u), (stats.Armor, stats.Evasion, stats.MaxWard));   // 30 x 0.1, 40 x 1.125, 50 x 0.25
        Assert.Equal(4f + 10f, stats.Offence.AttackSpeedPct);
    }

    /// <summary>
    /// #1178: defence is class-neutral. Armour, evasion and ward are gear plus Strength, Agility and Intellect times the
    /// combat formula's conversions, each product truncated once; block and resistances come from gear alone. A level-1
    /// Warrior is Strength 23, Agility 20, Intellect 20. A chest whose armour lines scale past a long saturates there, and
    /// Strength's armour added on top keeps it saturated rather than wrapping it to 0.
    /// </summary>
    [Theory]
    [InlineData("nothing", 2u, 2u, 5u, 0f)]
    [InlineData("the robe", 15u, 9u, 9u, 10f)]
    [InlineData("armour past a long", uint.MaxValue, 2u, 5u, 0f)]
    public void Turn_attributes_and_gear_into_armour_evasion_ward_and_resistances(
        string gear, uint armor, uint evasion, uint ward, float fire)
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

        var chest = new ItemTemplate { Name = "Chest", Slot = ItemSlotType.Chest };
        IReadOnlyList<WornItem> worn = gear switch
        {
            "nothing" => [],
            "the robe" => Worn.Of(robe),
            _ => [Rolled(chest, [.. Enumerable.Repeat((StatType.Armor, int.MaxValue, (int?)null), 4),
                .. Enumerable.Repeat((StatType.LocalIncreasedArmor, int.MaxValue, (int?)null), 4)])],
        };

        DerivedCharacterStats stats = CharacterStatsCalculator.Calculate(SeededRow(CharacterClass.Warrior, 1),
            worn, formula);

        Assert.Equal((armor, evasion, ward, 0f), (stats.Armor, stats.Evasion, stats.MaxWard, stats.BlockPct));
        Assert.Equal(new Resistances(fire, 0f, 0f, 0f), stats.Resistances);
    }

    /// <summary>
    /// #1179 (B2-R14): increased maximum life scales the flat life from base, Stamina and gear, in whole numbers, half away
    /// from zero, never below 0. BaseHp 500 and Stamina 20 at the seeded 5 life per Stamina is 600. 675 x 1.26 is 850.5,
    /// so 851. An aura folds after the increase: 600 x 1.10 is 660, then +50 flat is 710 (715 had it been scaled). Four
    /// rings at the largest lines the columns hold scale past a long, and the maximum saturates instead of wrapping.
    /// </summary>
    [Theory]
    [InlineData(0, 0, 600u, 0, 1)]
    [InlineData(75, 0, 675u, 0, 1)]
    [InlineData(0, 26, 756u, 0, 1)]
    [InlineData(75, 26, 851u, 0, 1)]
    [InlineData(0, -150, 0u, 0, 1)]
    [InlineData(0, 10, 710u, 50, 1)]
    [InlineData(int.MaxValue, int.MaxValue, uint.MaxValue, 0, 4)]
    public void Scale_maximum_health_by_increased_maximum_life(int gearHealth, int increased, uint expected, int auraHealth,
        int items)
    {
        var row = new ClassLevelStat { Class = CharacterClass.Warrior, Level = 1, BaseHp = 500, Stamina = 20 };
        var amulet = new ItemTemplate { Name = "Amulet", Slot = ItemSlotType.Neck };
        var aura = new AuraStatModifier { Stat = AuraStat.MaxHealth, Kind = AuraModifierKind.Flat, Value = auraHealth };
        AuraStatTotals? auras = auraHealth == 0
            ? null
            : AuraStatTotals.Of([aura], static m => ((IReadOnlyList<AuraStatModifier>)[m], 1u));

        DerivedCharacterStats stats = CharacterStatsCalculator.Calculate(row,
            [.. Enumerable.Repeat(Rolled(amulet, (StatType.Health, gearHealth, null), (StatType.IncreasedMaximumLife, increased, null)), items)],
            TestCombat.Formula, auras);

        Assert.Equal(expected, stats.MaxHealth);
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
    public void Add_flat_health_damage_and_crit()
    {
        DerivedCharacterStats warrior = Calc(
            SeededRow(CharacterClass.Warrior, 1), [EquipTemplates.Chestguard, EquipTemplates.VigorAmulet]);

        Assert.Equal(130u + 24u * 5u + 15u, warrior.MaxHealth);
        Assert.Equal(3f, warrior.Offence.Increased(OffenceStat.Fire));
        Assert.Equal(7.0f, warrior.CritPct);
    }

    /// <summary>
    /// #1181: maximum mana folds as life does. A seeded level-1 Warrior (Intellect 20) has 100 + 20 x 4 = 180. Flat power
    /// and Intellect (11 x 4 = 44) add to the flat sum, which increased maximum mana then scales, never below 0. Increased
    /// mana regeneration moves no maximum: it is summed for the regeneration rates. A huge Intellect conversion caps
    /// the maximum rather than wrapping it.
    /// </summary>
    [Theory]
    [InlineData("nothing", 180u, 0f)]
    [InlineData("+20 power", 200u, 0f)]
    [InlineData("+11 Intellect", 224u, 0f)]
    [InlineData("+50 % increased maximum mana", 270u, 0f)]
    [InlineData("+20 power and +50 %", 300u, 0f)]
    [InlineData("-150 %", 0u, 0f)]
    [InlineData("+25 % increased mana regeneration", 180u, 25f)]
    [InlineData("a huge Intellect conversion", uint.MaxValue, 0f)]
    public void Fold_flat_and_increased_maximum_mana_from_gear(string gear, uint maxMana, float increasedRegen)
    {
        (StatType Stat, int Value)[] lines = gear switch
        {
            "nothing" => [],
            "+20 power" => [(StatType.Power, 20)],
            "+11 Intellect" => [(StatType.Intellect, 11)],
            "+50 % increased maximum mana" => [(StatType.IncreasedMaximumMana, 50)],
            "+20 power and +50 %" => [(StatType.Power, 20), (StatType.IncreasedMaximumMana, 50)],
            "-150 %" => [(StatType.IncreasedMaximumMana, -150)],
            "a huge Intellect conversion" => [],
            _ => [(StatType.IncreasedManaRegeneration, 25)],
        };
        var amulet = new ItemTemplate { Name = "Amulet", Slot = ItemSlotType.Neck };
        CombatFormula formula = TestCombat.Formula;
        if (gear == "a huge Intellect conversion")
            formula.ManaPerIntellect = float.MaxValue;

        DerivedCharacterStats stats = CharacterStatsCalculator.Calculate(SeededRow(CharacterClass.Warrior, 1),
            lines.Length == 0 ? [] : [Rolled(amulet, [.. lines.Select(l => (l.Stat, l.Value, (int?)null))])],
            formula);

        Assert.Equal((maxMana, increasedRegen), (stats.MaxPower, stats.IncreasedManaRegenPct));
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
    /// Review Focus 4 (#1179, A-R13, A-R14, B2-R1): a local line counts only on the item it sits on, and scales the base
    /// stats its copy rolled. The plate chest's 10 % increased armour scales its own rolled 30 and its flat 5 together, 35 to
    /// 38.5, rounded half away from zero to 39, never the helmet's 10: 49, plus Strength 23's 2; the gloves' 100 % scales no
    /// armour of theirs. The shield's 8 block chance adds as it is, never
    /// scaled by its 50 % increased armour. The sword's rolled 3-7 at 700 ms takes its own 50 % increased physical damage
    /// and 10 % attack speed (4.5 and 10.5, rounded half away from zero to 5-11; 700 / 1.1 to 636) and so does its added
    /// physical damage (1-2 to 2-3), never the offence block; the ring's added damage to attacks and to spells is global.
    /// The boots' rolled 25 evasion at 82 % is exactly 45.5, rounded half away from zero to 46 in whole numbers (a double
    /// gives 45.49999… from 25 x (1 + 0.82)).
    /// </summary>
    [Theory]
    [InlineData("armor", 51d)]
    [InlineData("block", 8d)]
    [InlineData("evasion", 46d)]
    [InlineData("weapon min1", 5d)]
    [InlineData("weapon max1", 11d)]
    [InlineData("weapon added physical min", 2d)]
    [InlineData("weapon added physical max", 3d)]
    [InlineData("weapon added fire max", 5d)]
    [InlineData("attack time", 636d)]
    [InlineData("attacks physical max", 3d)]
    [InlineData("spells cold max", 2d)]
    [InlineData("global increased physical", 0d)]
    public void Keep_local_modifiers_on_the_item_they_sit_on(string what, double expected)
    {
        var formula = new CombatFormula { ArmorPerStrength = 0.1f };
        var head = new ItemTemplate { Name = "Head", Slot = ItemSlotType.Head };
        var chest = new ItemTemplate { Name = "Chest", Slot = ItemSlotType.Chest };
        var shield = new ItemTemplate { Name = "Shield", Slot = ItemSlotType.OffHand };
        var mainHand = new ItemTemplate { Name = "Sword", Slot = ItemSlotType.MainHand, DamageType = DamageType.Physical };
        var ring = new ItemTemplate { Name = "Ring", Slot = ItemSlotType.Finger };
        var gloves = new ItemTemplate { Name = "Gloves", Slot = ItemSlotType.Hands };
        var boots = new ItemTemplate { Name = "Boots", Slot = ItemSlotType.Feet };
        WornItem[] worn =
        [
            Rolled(head, new ItemBaseStats { Armour = 10 }),
            Rolled(chest, new ItemBaseStats { Armour = 30 }, (StatType.Armor, 5, null), (StatType.LocalIncreasedArmor, 10, null)),
            Rolled(shield, new ItemBaseStats { BlockChance = 8 }, (StatType.LocalIncreasedArmor, 50, null)),
            Rolled(mainHand, new ItemBaseStats { DamageMin = 3, DamageMax = 7, AttackTime = 700 },
                (StatType.AddedPhysicalDamage, 1, 2), (StatType.LocalIncreasedPhysicalDamage, 50, null),
                (StatType.AddedFireDamage, 2, 5), (StatType.LocalAttackSpeed, 10, null)),
            Rolled(ring, (StatType.AddedPhysicalDamageToAttacks, 1, 3), (StatType.AddedColdDamageToSpells, 1, 2)),
            Rolled(gloves, (StatType.LocalIncreasedArmor, 100, null)),
            Rolled(boots, new ItemBaseStats { Evasion = 25 }, (StatType.LocalIncreasedEvasion, 82, null)),
        ];

        DerivedCharacterStats stats = CharacterStatsCalculator.Calculate(SeededRow(CharacterClass.Warrior, 1), worn,
            formula);

        double actual = what switch
        {
            "armor" => stats.Armor,
            "block" => stats.BlockPct,
            "evasion" => stats.Evasion,
            "weapon min1" => stats.Weapon.Min1,
            "weapon max1" => stats.Weapon.Max1,
            "weapon added physical min" => stats.Weapon.Added.Min(DamageType.Physical),
            "weapon added physical max" => stats.Weapon.Added.Max(DamageType.Physical),
            "weapon added fire max" => stats.Weapon.Added.Max(DamageType.Fire),
            "attack time" => stats.Weapon.AttackTimeMs,
            "attacks physical max" => stats.AddedToAttacks.Max(DamageType.Physical),
            "spells cold max" => stats.AddedToSpells.Max(DamageType.Cold),
            _ => stats.Offence.Increased(OffenceStat.Physical),
        };
        Assert.Equal(expected, actual);
    }

    /// <summary>A copy of <paramref name="template" /> with no base stats whose modifiers hold <paramref name="values" />.</summary>
    private static WornItem Rolled(ItemTemplate template, params (StatType Stat, int Value, int? Value2)[] values) =>
        Rolled(template, default, values);

    /// <summary>
    /// A copy of <paramref name="template" /> that rolled <paramref name="baseStats" /> and whose modifiers hold
    /// <paramref name="values" />, two to a modifier.
    /// </summary>
    private static WornItem Rolled(ItemTemplate template, ItemBaseStats baseStats,
        params (StatType Stat, int Value, int? Value2)[] values)
    {
        var item = new ItemInstanceId(Guid.CreateVersion7());
        return new WornItem(template, baseStats, [.. values.Chunk(2).Select((pair, ordinal) => TestModifiers.Modifier(item, 1,
            (byte)ordinal, [.. pair.Select((v, i) => ((byte)i, v.Stat, v.Value, v.Value2))]))], template.DamageType);
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
