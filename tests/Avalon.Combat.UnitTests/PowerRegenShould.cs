using Avalon.World.Public.Enums;

namespace Avalon.Combat.UnitTests;

/// <summary>
/// The one power-regen rule, shared by CharacterEntity and the balance simulator. The fraction of a point is carried
/// between steps, so the per-second rate holds at any tick rate; before, each 1/60 s tick was floored up to a point.
/// </summary>
public class PowerRegenShould
{
    private const double Tick = 1d / 60d;

    private static readonly RegenConfiguration Config = new();   // 0.05 a stat a second in combat, 0.3 out of it

    private static DerivedCharacterStats Stats(uint intellect, uint agility) =>
        new(MaxHealth: 100, MaxPower: 100, Stamina: 0, Strength: 0, Agility: agility, Intellect: intellect, Armor: 0,
            BlockPct: 0f, DodgePct: 0f, CritPct: 0f, AttackDamage: 0, AbilityDamage: 0);

    [Theory]
    [InlineData(CharacterClass.Wizard, 23u)]
    [InlineData(CharacterClass.Healer, 23u)]
    [InlineData(CharacterClass.Hunter, 31u)]
    [InlineData(CharacterClass.Warrior, 0u)]
    public void Read_the_stat_each_class_regenerates_from(CharacterClass @class, uint expected) =>
        Assert.Equal(expected, PowerRegen.StatOf(@class, Stats(intellect: 23, agility: 31)));

    [Fact]
    public void Give_one_point_over_sixty_ticks_in_combat_and_carry_the_rest()
    {
        double carry = 0d;
        uint total = 0;
        for (int tick = 0; tick < 60; tick++)
            total += PowerRegen.Amount(Config, regenStat: 23, inCombat: true, castSuppressed: false, Tick, ref carry);

        // 23 x 0.05 = 1.15 a second: one whole point, 0.15 carried. The floor gave 60.
        Assert.Equal(1u, total);
        Assert.Equal(0.15, carry, precision: 3);
    }

    [Fact]
    public void Scale_by_the_out_of_combat_rate_and_keep_the_fraction()
    {
        double carry = 0d;

        Assert.Equal(6u, PowerRegen.Amount(Config, regenStat: 23, inCombat: false, castSuppressed: false, 1d, ref carry));
        Assert.Equal(0.9, carry, precision: 5);   // 6.9
    }

    [Fact]
    public void Give_the_same_points_in_sixty_ticks_as_in_one_second()
    {
        double carry = 0d;
        uint total = 0;
        for (int tick = 0; tick < 60; tick++)
            total += PowerRegen.Amount(Config, regenStat: 23, inCombat: false, castSuppressed: false, Tick, ref carry);

        Assert.Equal(6u, total);
        Assert.Equal(0.9, carry, precision: 3);
    }

    [Fact]
    public void Pay_a_carried_fraction_on_a_later_step()
    {
        double carry = 0.9;

        // 0.9 + 23 x 0.3 / 60 = 1.015
        Assert.Equal(1u, PowerRegen.Amount(Config, regenStat: 23, inCombat: false, castSuppressed: false, Tick, ref carry));
        Assert.Equal(0.015, carry, precision: 3);
    }

    [Fact]
    public void Give_nothing_and_drop_the_carry_while_a_cast_suppresses_it()
    {
        double carry = 0.5;

        Assert.Equal(0u, PowerRegen.Amount(Config, regenStat: 23, inCombat: true, castSuppressed: true, Tick, ref carry));
        Assert.Equal(0d, carry);
    }

    [Fact]
    public void Give_nothing_and_drop_the_carry_without_a_regen_stat()
    {
        double carry = 0.5;

        Assert.Equal(0u, PowerRegen.Amount(Config, regenStat: 0, inCombat: true, castSuppressed: false, Tick, ref carry));
        Assert.Equal(0d, carry);
    }
}
