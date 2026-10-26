using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;

namespace Avalon.Combat.UnitTests;

/// <summary>How aura modifiers fold into stats: flat added, then scaled by the increased sum and by each more, every modifier held once per stack.</summary>
public class AuraStatsShould
{
    private static AuraStatModifier Mod(AuraStat stat, AuraModifierKind kind, float value) =>
        new() { AuraId = new AuraId(1), Stat = stat, Kind = kind, Value = value };

    private static AuraStatTotals Of(params (AuraStatModifier Modifier, uint Stacks)[] auras) =>
        AuraStatTotals.Of(auras, static a => ((IReadOnlyList<AuraStatModifier>)[a.Modifier], a.Stacks));

    [Fact]
    public void Leave_stats_alone_with_no_aura()
    {
        var stats = new DerivedCharacterStats(500, 100, 20, 20, 20, 20, 30, 5f, 3, 5f, 0f);

        Assert.True(AuraStatTotals.Empty.IsEmpty);
        Assert.Equal(stats, AuraStats.Fold(stats, AuraStatTotals.Empty));
    }

    public static TheoryData<uint, (AuraModifierKind Kind, float Value, uint Stacks)[], uint> ArmourFolds() => new()
    {
        // Fortified: +20 % increased on 30 is 36; Sundered on top (-25 %) sums to 30 x (1 + (20 - 25) / 100) = 28.5, 29.
        { 30u, [(AuraModifierKind.Increased, 20f, 1u)], 36u },
        { 30u, [(AuraModifierKind.Increased, 20f, 1u), (AuraModifierKind.Increased, -25f, 1u)], 29u },
        // Each More multiplies on its own: (100 + 20) x 1.5 x 1.2 x 1.1 = 237.6, 238.
        { 100u, [(AuraModifierKind.Flat, 20f, 1u), (AuraModifierKind.Increased, 50f, 1u), (AuraModifierKind.More, 20f, 1u),
            (AuraModifierKind.More, 10f, 1u)], 238u },
        // A stacked More multiplies once per stack: 100 x 1.1 x 1.1 = 121.
        { 100u, [(AuraModifierKind.More, 10f, 2u)], 121u },
    };

    /// <summary>A stat folds as <c>(base + flat) x (1 + increased / 100) x each (1 + more / 100)</c>, one aura per modifier.</summary>
    [Theory]
    [MemberData(nameof(ArmourFolds))]
    public void Fold_armour_as_base_plus_flat_times_the_increased_sum_times_each_more(uint armour,
        (AuraModifierKind Kind, float Value, uint Stacks)[] auras, uint expected)
    {
        var totals = AuraStatTotals.Of(auras,
            static a => ((IReadOnlyList<AuraStatModifier>)[Mod(AuraStat.Armor, a.Kind, a.Value)], a.Stacks));

        Assert.Equal(expected, AuraStats.Apply(armour, totals, AuraStat.Armor));
    }

    /// <summary>Crippled: movement speed is in points, so Flat -30 slows a gearless unit by 30 %.</summary>
    [Fact]
    public void Add_flat_points_to_a_percentage_stat()
    {
        AuraStatTotals crippled = Of((Mod(AuraStat.MovementSpeed, AuraModifierKind.Flat, -30f), 1));

        Assert.Equal(-30f, AuraStats.Apply(0f, crippled, AuraStat.MovementSpeed));
        Assert.Equal(-20f, AuraStats.Apply(10f, crippled, AuraStat.MovementSpeed));
    }

    [Fact]
    public void Multiply_a_modifier_by_its_stacks() =>
        Assert.Equal(61u, AuraStats.Apply(46u, Of((Mod(AuraStat.Armor, AuraModifierKind.Flat, 5f), 3)), AuraStat.Armor));

    [Fact]
    public void Never_go_below_zero_and_never_take_the_last_point_of_health()
    {
        AuraStatTotals drain = Of((Mod(AuraStat.Armor, AuraModifierKind.Flat, -100f), 1),
            (Mod(AuraStat.MaxHealth, AuraModifierKind.Flat, -1000f), 1));
        var stats = new DerivedCharacterStats(500, 100, 20, 20, 20, 20, 30, 5f, 3, 5f);

        DerivedCharacterStats folded = AuraStats.Fold(stats, drain);

        Assert.Equal((0u, 1u), (folded.Armor, folded.MaxHealth));
    }

    [Fact]
    public void Fold_every_stat_an_aura_names_on_a_character()
    {
        AuraStatTotals all = Of(
            (Mod(AuraStat.Armor, AuraModifierKind.Flat, 10f), 1), (Mod(AuraStat.CritPct, AuraModifierKind.Flat, 2f), 1),
            (Mod(AuraStat.Evasion, AuraModifierKind.Flat, 3f), 1), (Mod(AuraStat.BlockPct, AuraModifierKind.Flat, 4f), 1),
            (Mod(AuraStat.AttackSpeed, AuraModifierKind.Flat, 10f), 1), (Mod(AuraStat.MovementSpeed, AuraModifierKind.Flat, -30f), 1),
            (Mod(AuraStat.MaxHealth, AuraModifierKind.Increased, 10f), 1), (Mod(AuraStat.MaxPower, AuraModifierKind.Flat, 5f), 1),
            (Mod(AuraStat.MaxWard, AuraModifierKind.Increased, 50f), 1), (Mod(AuraStat.FireResistance, AuraModifierKind.Flat, -30f), 1));
        var stats = new DerivedCharacterStats(500, 100, 20, 20, 20, 20, 30, 5f, 3, 5f, 0f,
            MaxWard: 40, Resistances: new Resistances(10f, 0f, 0f, 0f),
            Weapon: new WeaponDamage(4, 7, null, 0, 0, null, 800));

        DerivedCharacterStats f = AuraStats.Fold(stats, all);

        Assert.Equal((550u, 105u, 40u), (f.MaxHealth, f.MaxPower, f.Armor));
        Assert.Equal((7f, 6u, 9f, -30f), (f.CritPct, f.Evasion, f.BlockPct, f.MovementSpeedPct));
        // #1178: a flat -30 fire resistance on 10 is -20, a debuff below 0.
        Assert.Equal((60u, new Resistances(-20f, 0f, 0f, 0f)), (f.MaxWard, f.Resistances));
        Assert.Equal((20u, stats.Weapon), (f.Stamina, f.Weapon));   // untouched
        // #1178: the offence block was built with the auras (CharacterOffence), so this fold leaves it alone.
        Assert.Equal(stats.Offence, f.Offence);
    }

    [Fact]
    public void Fold_a_creatures_attack_and_defence()
    {
        AuraStatTotals sundered = Of((Mod(AuraStat.Armor, AuraModifierKind.Increased, -25f), 1),
            (Mod(AuraStat.CritPct, AuraModifierKind.Flat, 5f), 1), (Mod(AuraStat.FireResistance, AuraModifierKind.Flat, -30f), 1));

        Assert.Equal(new DefenderCombat(30, 2, 1f, new Resistances(-20f, 0f, 0f, 50f)),
            AuraStats.Fold(new DefenderCombat(40, 2, 1f, new Resistances(10f, 0f, 0f, 50f)), sundered));
        var natural = new WeaponDamage(5, 9, null, 0, 0, null, 2250);
        Assert.Equal(new AttackerCombat(3, 10f, natural), AuraStats.Fold(new AttackerCombat(3, 5f, natural), sundered));
    }

    /// <summary>The calculator folds what auras hold after gear: 24 armour of plate under Fortified is round(28.8) = 29.</summary>
    [Fact]
    public void Fold_auras_into_a_characters_stats_after_gear()
    {
        var row = new ClassLevelStat
        { Class = CharacterClass.Warrior, Level = 1, BaseHp = 130, Stamina = 22, Strength = 23, Agility = 20, Intellect = 20 };
        var plate = new ItemTemplate { Name = "Plate", Slot = ItemSlotType.Chest, StatType1 = StatType.Armor, StatValue1 = 24 };

        DerivedCharacterStats folded = CharacterStatsCalculator.Calculate(row, Worn.Of(plate), new CombatFormula(),
            Of((Mod(AuraStat.Armor, AuraModifierKind.Increased, 20f), 1)));

        Assert.Equal(29u, folded.Armor);
        Assert.Equal(CharacterStatsCalculator.Calculate(row, Worn.Of(plate), new CombatFormula()).Offence, folded.Offence);
    }
}
