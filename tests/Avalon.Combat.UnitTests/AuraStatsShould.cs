using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Public.Enums;

namespace Avalon.Combat.UnitTests;

/// <summary>How aura modifiers fold into stats: flat added, then percent scaled, each times the stacks held.</summary>
public class AuraStatsShould
{
    private static AuraStatModifier Mod(AuraStat stat, AuraModifierKind kind, float value) =>
        new() { AuraId = new AuraId(1), Stat = stat, Kind = kind, Value = value };

    private static AuraStatTotals Of(params (AuraStatModifier Modifier, uint Stacks)[] auras) =>
        AuraStatTotals.Of(auras.Select(a => ((IReadOnlyList<AuraStatModifier>)[a.Modifier], a.Stacks)));

    [Fact]
    public void Leave_stats_alone_with_no_aura()
    {
        var stats = new DerivedCharacterStats(500, 100, 20, 20, 20, 20, 30, 5f, 3f, 5f, 46, 9, 4, 7, 3f, 0f);

        Assert.True(AuraStatTotals.Empty.IsEmpty);
        Assert.Equal(stats, AuraStats.Fold(stats, AuraStatTotals.Empty));
    }

    /// <summary>Fortified: armour +20 % on 30 is 36; Sundered on top (-25 %) gives 30 x (1 + (20 - 25) / 100) = 28.5, 29.</summary>
    [Fact]
    public void Scale_armour_by_the_sum_of_every_percent_held()
    {
        Assert.Equal(36u, AuraStats.Apply(30u, Of((Mod(AuraStat.Armor, AuraModifierKind.Percent, 20f), 1)), AuraStat.Armor));
        Assert.Equal(29u, AuraStats.Apply(30u, Of((Mod(AuraStat.Armor, AuraModifierKind.Percent, 20f), 1),
            (Mod(AuraStat.Armor, AuraModifierKind.Percent, -25f), 1)), AuraStat.Armor));
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
        Assert.Equal(61u, AuraStats.Apply(46u, Of((Mod(AuraStat.AttackDamage, AuraModifierKind.Flat, 5f), 3)), AuraStat.AttackDamage));

    [Fact]
    public void Never_go_below_zero_and_never_take_the_last_point_of_health()
    {
        AuraStatTotals drain = Of((Mod(AuraStat.Armor, AuraModifierKind.Flat, -100f), 1),
            (Mod(AuraStat.MaxHealth, AuraModifierKind.Flat, -1000f), 1));
        var stats = new DerivedCharacterStats(500, 100, 20, 20, 20, 20, 30, 5f, 3f, 5f, 46, 9);

        DerivedCharacterStats folded = AuraStats.Fold(stats, drain);

        Assert.Equal((0u, 1u), (folded.Armor, folded.MaxHealth));
    }

    [Fact]
    public void Fold_every_stat_an_aura_names_on_a_character()
    {
        AuraStatTotals all = Of(
            (Mod(AuraStat.Armor, AuraModifierKind.Flat, 10f), 1), (Mod(AuraStat.AttackDamage, AuraModifierKind.Percent, 50f), 1),
            (Mod(AuraStat.AbilityDamage, AuraModifierKind.Flat, 1f), 1), (Mod(AuraStat.CritPct, AuraModifierKind.Flat, 2f), 1),
            (Mod(AuraStat.DodgePct, AuraModifierKind.Flat, 3f), 1), (Mod(AuraStat.BlockPct, AuraModifierKind.Flat, 4f), 1),
            (Mod(AuraStat.HastePct, AuraModifierKind.Flat, 10f), 1), (Mod(AuraStat.MovementSpeed, AuraModifierKind.Flat, -30f), 1),
            (Mod(AuraStat.MaxHealth, AuraModifierKind.Percent, 10f), 1), (Mod(AuraStat.MaxPower, AuraModifierKind.Flat, 5f), 1));
        var stats = new DerivedCharacterStats(500, 100, 20, 20, 20, 20, 30, 5f, 3f, 5f, 46, 9, 4, 7, 3f, 0f);

        DerivedCharacterStats f = AuraStats.Fold(stats, all);

        Assert.Equal((550u, 105u, 40u, 69u, 10u), (f.MaxHealth, f.MaxPower, f.Armor, f.AttackDamage, f.AbilityDamage));
        Assert.Equal((7f, 6f, 9f, 13f, -30f), (f.CritPct, f.DodgePct, f.BlockPct, f.HastePct, f.MovementSpeedPct));
        Assert.Equal((20u, 4u, 7u), (f.Stamina, f.WeaponMin, f.WeaponMax));   // untouched
    }

    [Fact]
    public void Fold_a_creatures_attack_and_defence()
    {
        AuraStatTotals sundered = Of((Mod(AuraStat.Armor, AuraModifierKind.Percent, -25f), 1),
            (Mod(AuraStat.CritPct, AuraModifierKind.Flat, 5f), 1));

        Assert.Equal(new DefenderCombat(30, 2f, 1f), AuraStats.Fold(new DefenderCombat(40, 2f, 1f), sundered));
        Assert.Equal(new AttackerCombat(3, 0, 0, 10f, 5, 9), AuraStats.Fold(new AttackerCombat(3, 0, 0, 5f, 5, 9), sundered));
    }

    /// <summary>The calculator folds what auras hold after gear: 24 armour of plate under Fortified is round(28.8) = 29.</summary>
    [Fact]
    public void Fold_auras_into_a_characters_stats_after_gear()
    {
        var row = new ClassLevelStat
            { Class = CharacterClass.Warrior, Level = 1, BaseHp = 20, Stamina = 22, Strength = 23, Agility = 20, Intellect = 20 };
        var factors = new ClassStatFactors { Class = CharacterClass.Warrior, HpPerStamina = 10, FixedPower = 100, AttackPerStrength = 2 };
        var plate = new ItemTemplate { Name = "Plate", Slot = ItemSlotType.Chest, StatType1 = StatType.Armor, StatValue1 = 24 };

        DerivedCharacterStats folded = CharacterStatsCalculator.Calculate(row, [plate], factors,
            Of((Mod(AuraStat.Armor, AuraModifierKind.Percent, 20f), 1)));

        Assert.Equal(29u, folded.Armor);
        Assert.Equal(CharacterStatsCalculator.Calculate(row, [plate], factors).AttackDamage, folded.AttackDamage);
    }
}
