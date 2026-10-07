using Avalon.Combat;
using Avalon.Database.World.Seeding;
using Avalon.Domain.World;

namespace Avalon.Server.World.UnitTests.Characters;

/// <summary>What a hit reads from derived stats (#506), in one place for the entity and the balance simulator.</summary>
public class DerivedCombatStatsShould
{
    private static readonly DerivedCharacterStats s_warrior =
        new(MaxHealth: 240, MaxPower: 100, Stamina: 22, Strength: 23, Agility: 20, Intellect: 20, Armor: 24,
            BlockPct: 5f, DodgePct: 3.664f, CritPct: 5f, AttackDamage: 46, AbilityDamage: 4, WeaponMin: 7, WeaponMax: 11,
            HastePct: 80f);

    [Fact]
    public void Cap_haste_at_the_formula_cap()
    {
        CombatFormula formula = CombatSeed.Formula();   // HasteCap 50

        Assert.Equal(50f, s_warrior.EffectiveHastePct(formula));
        Assert.Equal(0f, (s_warrior with { HastePct = -5f }).EffectiveHastePct(formula));
    }
}
