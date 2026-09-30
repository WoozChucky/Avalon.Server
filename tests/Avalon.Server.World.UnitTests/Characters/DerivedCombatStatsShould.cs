using Avalon.Database.World.Seeding;
using Avalon.Domain.World;
using Avalon.World.Characters;
using Avalon.World.Combat;
using Avalon.World.Creatures;
using Xunit;
using Avalon.Combat;

namespace Avalon.Server.World.UnitTests.Characters;

/// <summary>What a hit reads from derived stats (#506), in one place for the entity and the balance simulator.</summary>
public class DerivedCombatStatsShould
{
    private static readonly DerivedCharacterStats Warrior =
        new(MaxHealth: 240, MaxPower: 100, Stamina: 22, Strength: 23, Agility: 20, Intellect: 20, Armor: 24,
            BlockPct: 5f, DodgePct: 3.664f, CritPct: 5f, AttackDamage: 46, AbilityDamage: 4, WeaponMin: 7, WeaponMax: 11,
            HastePct: 80f);

    [Fact]
    public void Attack_with_the_level_damage_stats_crit_and_main_hand() =>
        Assert.Equal(new AttackerCombat(3, 46, 4, 5f, 7, 11), Warrior.AttackerAt(3));

    [Fact]
    public void Defend_with_armour_dodge_and_block() =>
        Assert.Equal(new DefenderCombat(24, 3.664f, 5f), Warrior.Defence);

    [Fact]
    public void Cap_haste_at_the_formula_cap()
    {
        CombatFormula formula = CombatSeed.Formula();   // HasteCap 50

        Assert.Equal(50f, Warrior.EffectiveHastePct(formula));
        Assert.Equal(0f, (Warrior with { HastePct = -5f }).EffectiveHastePct(formula));
    }

    [Fact]
    public void Give_a_creature_its_natural_range_and_rarity_chances()
    {
        var stats = new DerivedCreatureStats(Level: 4, Health: 92, DamageMin: 7, DamageMax: 11, Experience: 60,
            Armor: 10, CritPct: 5f, DodgePct: 3f, BlockPct: 1f);

        Assert.Equal(new AttackerCombat(4, 0, 0, 5f, 7, 11), stats.Attacker);
        Assert.Equal(new DefenderCombat(10, 3f, 1f), stats.Defence);
    }
}
