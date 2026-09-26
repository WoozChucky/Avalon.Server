using Avalon.Common.ValueObjects;
using Avalon.Database.World;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Handlers;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Characters;
using Avalon.World.Public.Enums;
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
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        return context.ClassLevelStats.AsNoTracking().ToList().Single(s => s.Class == @class && s.Level == level);
    }

    [Theory]
    [InlineData(CharacterClass.Warrior, 1, 240u, 100u, 46u, 4u)]
    [InlineData(CharacterClass.Warrior, 5, 400u, 100u, 62u, 4u)]
    [InlineData(CharacterClass.Wizard, 1, 121u, 365u, 10u, 69u)]
    [InlineData(CharacterClass.Wizard, 5, 205u, 565u, 10u, 93u)]
    [InlineData(CharacterClass.Hunter, 1, 178u, 68u, 45u, 10u)]
    [InlineData(CharacterClass.Hunter, 5, 282u, 114u, 59u, 10u)]
    [InlineData(CharacterClass.Healer, 1, 158u, 296u, 10u, 46u)]
    [InlineData(CharacterClass.Healer, 5, 258u, 472u, 10u, 62u)]
    [InlineData(CharacterClass.Warrior, 10, 600u, 100u, 82u, 4u)]
    [InlineData(CharacterClass.Hunter, 10, 412u, 172u, 76u, 10u)]
    public void Derive_the_seeded_numbers_for_each_class_with_no_gear(
        CharacterClass @class, ushort level, uint health, uint power, uint attack, uint ability)
    {
        ClassLevelStat row = SeededRow(@class, level);

        DerivedCharacterStats stats = CharacterStatsCalculator.Calculate(row, []);

        Assert.Equal(health, stats.MaxHealth);
        Assert.Equal(power, stats.MaxPower);
        Assert.Equal(attack, stats.AttackDamage);
        Assert.Equal(ability, stats.AbilityDamage);
        Assert.Equal((row.Stamina, row.Strength, row.Agility, row.Intellect),
            (stats.Stamina, stats.Strength, stats.Agility, stats.Intellect));
        Assert.Equal(0u, stats.Armor);
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

        Assert.Equal((200u, 0u, 40u, 41u, 33u, 20u),
            (row.BaseHp, row.BaseMana, row.Stamina, row.Strength, row.Agility, row.Intellect));
    }

    [Theory]
    [InlineData(CharacterClass.Warrior, 5.0f, 3.664f, 5.0f)]
    [InlineData(CharacterClass.Wizard, 0f, 3.25f, 1.85f)]
    [InlineData(CharacterClass.Hunter, 0f, 4.35f, 5.0f)]
    [InlineData(CharacterClass.Healer, 0f, 3.25f, 1.85f)]
    public void Start_each_class_at_its_base_block_dodge_and_crit(CharacterClass @class, float block, float dodge, float crit)
    {
        DerivedCharacterStats stats = CharacterStatsCalculator.Calculate(SeededRow(@class, 1), []);

        Assert.Equal((block, dodge, crit), (stats.BlockPct, stats.DodgePct, stats.CritPct));
    }

    [Fact]
    public void Add_worn_attributes_before_deriving_health_and_damage()
    {
        // Warrior 1: BaseHp 20, Stamina 22, Strength 23. The chestguard adds Str 2, Armor 8, Sta 2.
        DerivedCharacterStats stats =
            CharacterStatsCalculator.Calculate(SeededRow(CharacterClass.Warrior, 1), [EquipTemplates.Chestguard]);

        Assert.Equal(24u, stats.Stamina);
        Assert.Equal(25u, stats.Strength);
        Assert.Equal(20u + 24u * 10u, stats.MaxHealth);
        Assert.Equal(50u, stats.AttackDamage);
        Assert.Equal(8u, stats.Armor);
    }

    [Fact]
    public void Add_flat_health_damage_and_crit_but_never_change_a_warriors_power()
    {
        DerivedCharacterStats warrior = CharacterStatsCalculator.Calculate(
            SeededRow(CharacterClass.Warrior, 1), [EquipTemplates.Chestguard, EquipTemplates.VigorAmulet]);

        Assert.Equal(20u + 24u * 10u + 15u, warrior.MaxHealth);
        Assert.Equal(100u, warrior.MaxPower);
        Assert.Equal(53u, warrior.AttackDamage);
        Assert.Equal(7.0f, warrior.CritPct);
    }

    [Fact]
    public void Add_flat_power_for_a_class_whose_pool_grows()
    {
        DerivedCharacterStats wizard =
            CharacterStatsCalculator.Calculate(SeededRow(CharacterClass.Wizard, 1), [EquipTemplates.VigorAmulet]);

        Assert.Equal(121u + 15u, wizard.MaxHealth);
        Assert.Equal(365u + 20u, wizard.MaxPower);
        Assert.Equal(13u, wizard.AttackDamage);
        Assert.Equal(1.85f + 2f, wizard.CritPct, precision: 4);
    }

    [Fact]
    public void Ignore_attack_speed_and_movement_speed()
    {
        var boots = new ItemTemplate
        {
            Id = new ItemTemplateId(700), Name = "Boots", Slot = ItemSlotType.Feet,
            StatType1 = StatType.MovementSpeed, StatValue1 = 50, StatType2 = StatType.AttackSpeed, StatValue2 = 9,
        };
        ClassLevelStat row = SeededRow(CharacterClass.Warrior, 1);

        Assert.Equal(CharacterStatsCalculator.Calculate(row, []), CharacterStatsCalculator.Calculate(row, [boots]));
    }

    [Fact]
    public void Read_all_ten_stat_pairs_and_skip_a_half_filled_one()
    {
        var trinket = new ItemTemplate
        {
            Id = new ItemTemplateId(701), Name = "Trinket", Slot = ItemSlotType.Neck,
            StatType1 = StatType.Armor, StatValue1 = 1, StatType10 = StatType.Armor, StatValue10 = 2,
            StatType5 = StatType.Armor, StatValue5 = null,
        };

        Assert.Equal(3u, CharacterStatsCalculator.Calculate(SeededRow(CharacterClass.Hunter, 1), [trinket]).Armor);
    }

    [Fact]
    public void Write_every_value_to_the_character_stats_row()
    {
        DerivedCharacterStats stats = new(MaxHealth: 260, MaxPower: 100, Stamina: 24, Strength: 25, Agility: 20,
            Intellect: 20, Armor: 8, BlockPct: 5f, DodgePct: 3.664f, CritPct: 5f, AttackDamage: 50, AbilityDamage: 4);

        CharacterStats row = stats.ToRow(new CharacterId(7));

        Assert.Equal(new CharacterId(7), row.CharacterId);
        Assert.Equal((260u, 100u, 0u), (row.MaxHealth, row.MaxPower1, row.MaxPower2));
        Assert.Equal((24u, 25u, 20u, 20u), (row.Stamina, row.Strength, row.Agility, row.Intellect));
        Assert.Equal((8u, 5f, 3.664f, 5f), (row.Armor, row.BlockPct, row.DodgePct, row.CritPct));
        Assert.Equal((50u, 4u), (row.AttackDamage, row.AbilityDamage));
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
