using Avalon.Database.World.Seeding;
using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;
using Avalon.World.Combat;
using Avalon.World.Public.Abilities;
using Xunit;

namespace Avalon.Server.World.UnitTests.Combat;

/// <summary>
/// The hit maths (#506): the base, then dodge, crit, block and armour, floored with a minimum of 1. The
/// seeded formula: ArmorBase 50, ArmorPerLevel 10, ArmorCap 0.75, crit 1.5, block 0.5, caps 50/30/50.
/// </summary>
public class HitResolverShould
{
    private static readonly CombatFormula Formula = CombatSeed.Formula();

    private static AttackerCombat Attacker(uint level = 1, uint attack = 0, uint ability = 0, float crit = 0f,
        uint weaponMin = 0, uint weaponMax = 0) => new(level, attack, ability, crit, weaponMin, weaponMax);

    private static DefenderCombat Defender(uint armor = 0, float dodge = 0f, float block = 0f) => new(armor, dodge, block);

    // ---- the base ----

    [Fact]
    public void Add_the_scaled_stat_alone_when_the_skill_has_no_weapon_term()
    {
        var rng = new ScriptedCombatRandom();

        float b = HitResolver.AbilityBase(Attacker(attack: 46, weaponMin: 4, weaponMax: 7), 12f, ScalingStat.Attack, 0.3f, 0f, rng);

        Assert.Equal(25.8f, b, precision: 4);
        Assert.Empty(rng.WeaponRolls);
    }

    [Fact]
    public void Add_the_weapon_roll_alone_when_the_skill_has_no_scaling()
    {
        var rng = new ScriptedCombatRandom().Longs(6);

        float b = HitResolver.AbilityBase(Attacker(attack: 46, weaponMin: 4, weaponMax: 7), 12f, ScalingStat.Attack, 0f, 1.5f, rng);

        Assert.Equal(21f, b, precision: 4);
        Assert.Equal([(4L, 7L)], rng.WeaponRolls);
    }

    [Fact]
    public void Roll_no_weapon_when_none_is_worn()
    {
        var rng = new ScriptedCombatRandom();

        float b = HitResolver.AbilityBase(Attacker(attack: 46), 12f, ScalingStat.Attack, 0.3f, 1f, rng);

        Assert.Equal(25.8f, b, precision: 4);
        Assert.Empty(rng.WeaponRolls);
    }

    [Fact]
    public void Scale_a_spell_with_ability_damage_and_ignore_attack_damage()
    {
        var rng = new ScriptedCombatRandom();

        float b = HitResolver.AbilityBase(Attacker(attack: 1000, ability: 69), 12f, ScalingStat.Ability, 0.25f, 0f, rng);

        Assert.Equal(12f + 0.25f * 69f, b, precision: 4);
    }

    [Fact]
    public void Add_all_three_terms_for_cleave_with_a_starter_sword()
    {
        var rng = new ScriptedCombatRandom().Longs(5);

        float b = HitResolver.AbilityBase(Attacker(attack: 46, weaponMin: 4, weaponMax: 7), 12f, ScalingStat.Attack, 0.3f, 1f, rng);

        Assert.Equal(30.8f, b, precision: 4);
    }

    // ---- each roll, just under its chance (it procs) and at it (it does not) ----

    [Fact]
    public void Dodge_a_roll_just_under_the_chance_and_not_one_at_it()
    {
        (uint dmg, HitResult result) = HitResolver.ResolveDamage(Attacker(), Defender(dodge: 10f), 100f, Formula,
            new ScriptedCombatRandom(0.0999));
        Assert.Equal((0u, HitResult.Dodged), (dmg, result));

        (dmg, result) = HitResolver.ResolveDamage(Attacker(), Defender(dodge: 10f), 100f, Formula,
            new ScriptedCombatRandom(0.10, 0.99, 0.99));
        Assert.Equal((100u, HitResult.None), (dmg, result));
    }

    [Fact]
    public void Crit_a_roll_just_under_the_chance_and_not_one_at_it()
    {
        (uint dmg, HitResult result) = HitResolver.ResolveDamage(Attacker(crit: 20f), Defender(), 100f, Formula,
            new ScriptedCombatRandom(0.99, 0.1999, 0.99));
        Assert.Equal((150u, HitResult.Crit), (dmg, result));

        (dmg, result) = HitResolver.ResolveDamage(Attacker(crit: 20f), Defender(), 100f, Formula,
            new ScriptedCombatRandom(0.99, 0.20, 0.99));
        Assert.Equal((100u, HitResult.None), (dmg, result));
    }

    [Fact]
    public void Block_a_roll_just_under_the_chance_and_not_one_at_it()
    {
        (uint dmg, HitResult result) = HitResolver.ResolveDamage(Attacker(), Defender(block: 5f), 100f, Formula,
            new ScriptedCombatRandom(0.99, 0.99, 0.0499));
        Assert.Equal((50u, HitResult.Blocked), (dmg, result));

        (dmg, result) = HitResolver.ResolveDamage(Attacker(), Defender(block: 5f), 100f, Formula,
            new ScriptedCombatRandom(0.99, 0.99, 0.05));
        Assert.Equal((100u, HitResult.None), (dmg, result));
    }

    // ---- the caps ----

    [Fact]
    public void Cap_dodge_at_the_formulas_dodge_cap()
    {
        (uint dmg, HitResult result) = HitResolver.ResolveDamage(Attacker(), Defender(dodge: 80f), 100f, Formula,
            new ScriptedCombatRandom(0.35, 0.99, 0.99));
        Assert.Equal((100u, HitResult.None), (dmg, result));

        (_, result) = HitResolver.ResolveDamage(Attacker(), Defender(dodge: 80f), 100f, Formula, new ScriptedCombatRandom(0.2999));
        Assert.Equal(HitResult.Dodged, result);
    }

    [Fact]
    public void Cap_crit_at_the_formulas_crit_cap()
    {
        (_, HitResult result) = HitResolver.ResolveDamage(Attacker(crit: 90f), Defender(), 100f, Formula,
            new ScriptedCombatRandom(0.99, 0.55, 0.99));
        Assert.Equal(HitResult.None, result);
    }

    [Fact]
    public void Cap_block_at_the_formulas_block_cap()
    {
        (_, HitResult result) = HitResolver.ResolveDamage(Attacker(), Defender(block: 90f), 100f, Formula,
            new ScriptedCombatRandom(0.99, 0.99, 0.55));
        Assert.Equal(HitResult.None, result);
    }

    // ---- the order ----

    [Fact]
    public void Stop_at_a_dodge_and_draw_nothing_after_it()
    {
        var rng = new ScriptedCombatRandom(0.0);

        (uint dmg, HitResult result) = HitResolver.ResolveDamage(Attacker(crit: 50f), Defender(armor: 24, dodge: 10f, block: 50f),
            100f, Formula, rng);

        Assert.Equal((0u, HitResult.Dodged), (dmg, result));
        Assert.Equal(1, rng.DoublesDrawn);
    }

    [Fact]
    public void Apply_crit_then_block_then_armour()
    {
        // floor(100 x 1.5 x 0.5 x (1 - 24 / (24 + 50 + 10 x 1))) = floor(75 x 0.7142857) = 53
        (uint dmg, HitResult result) = HitResolver.ResolveDamage(Attacker(level: 1, crit: 50f),
            Defender(armor: 24, block: 50f), 100f, Formula, new ScriptedCombatRandom(0.99, 0.0, 0.0));

        Assert.Equal(53u, dmg);
        Assert.Equal(HitResult.Crit | HitResult.Blocked, result);
    }

    // ---- armour ----

    [Fact]
    public void Reduce_by_the_formula_at_levels_one_and_ten()
    {
        (uint atOne, _) = HitResolver.ResolveDamage(Attacker(level: 1), Defender(armor: 24), 100f, Formula, ScriptedCombatRandom.Plain());
        (uint atTen, _) = HitResolver.ResolveDamage(Attacker(level: 10), Defender(armor: 24), 100f, Formula, ScriptedCombatRandom.Plain());

        Assert.Equal(71u, atOne);  // floor(100 x (1 - 24 / 84))
        Assert.Equal(86u, atTen);  // floor(100 x (1 - 24 / 174))
    }

    [Fact]
    public void Never_reduce_by_more_than_the_armour_cap()
    {
        (uint dmg, _) = HitResolver.ResolveDamage(Attacker(level: 1), Defender(armor: 10000), 100f, Formula, ScriptedCombatRandom.Plain());

        Assert.Equal(25u, dmg);
    }

    [Fact]
    public void Reduce_nothing_with_no_armour_whatever_the_attackers_level()
    {
        (uint dmg, _) = HitResolver.ResolveDamage(Attacker(level: uint.MaxValue), Defender(armor: 0), 100f, Formula,
            ScriptedCombatRandom.Plain());

        Assert.Equal(100u, dmg);
    }

    [Fact]
    public void Stay_finite_against_an_attacker_of_the_highest_level()
    {
        (uint dmg, _) = HitResolver.ResolveDamage(Attacker(level: uint.MaxValue), Defender(armor: 24), 100f, Formula,
            ScriptedCombatRandom.Plain());

        Assert.Equal(99u, dmg);
    }

    // ---- the floor ----

    [Fact]
    public void Deal_at_least_one_to_a_hit_that_lands()
    {
        (uint dmg, HitResult result) = HitResolver.ResolveDamage(Attacker(), Defender(), 0.5f, Formula, ScriptedCombatRandom.Plain());

        Assert.Equal((1u, HitResult.None), (dmg, result));
    }

    [Fact]
    public void Deal_at_least_one_even_through_the_armour_cap_and_a_block()
    {
        (uint dmg, _) = HitResolver.ResolveDamage(Attacker(), Defender(armor: 10000, block: 50f), 1f, Formula,
            new ScriptedCombatRandom(0.99, 0.99, 0.0));

        Assert.Equal(1u, dmg);
    }

    [Fact]
    public void Deal_nothing_and_say_dodged_on_a_dodge()
    {
        (uint dmg, HitResult result) = HitResolver.ResolveDamage(Attacker(), Defender(dodge: 30f), 1000f, Formula,
            new ScriptedCombatRandom(0.0));

        Assert.Equal(0u, dmg);
        Assert.Equal(HitResult.Dodged, result);
    }

    [Fact]
    public void Floor_the_damage()
    {
        (uint dmg, _) = HitResolver.ResolveDamage(Attacker(), Defender(), 30.99f, Formula, ScriptedCombatRandom.Plain());

        Assert.Equal(30u, dmg);
    }

    // ---- heals ----

    [Fact]
    public void Crit_a_heal()
    {
        (uint heal, HitResult result) = HitResolver.ResolveHeal(Attacker(crit: 20f), 40f, Formula, new ScriptedCombatRandom(0.1));

        Assert.Equal((60u, HitResult.Crit), (heal, result));
    }

    [Fact]
    public void Draw_only_the_crit_roll_for_a_heal_and_never_mitigate_it()
    {
        var rng = new ScriptedCombatRandom(0.99);

        (uint heal, HitResult result) = HitResolver.ResolveHeal(Attacker(crit: 20f), 40.7f, Formula, rng);

        Assert.Equal((40u, HitResult.None), (heal, result));
        Assert.Equal(1, rng.DoublesDrawn);
    }
}
