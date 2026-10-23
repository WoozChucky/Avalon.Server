using Avalon.Combat;
using Avalon.Database.World.Seeding;
using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;

namespace Avalon.Server.World.UnitTests.Combat;

/// <summary>
/// The hit maths (#506, #1178): from the base (OffenceMathShould's), evasion, crit, block and each type's mitigation (armour for physical,
/// resistance for an element), floored with a minimum of 1. The seeded formula: ArmorBase 50, ArmorPerLevel 10,
/// ArmorCap 0.75, crit 1.5, block 0.5, crit and block caps 50; the evasion and resistance constants are set here.
/// </summary>
public class HitResolverShould
{
    /// <summary>The seeded formula with the defence constants pinned, so later tuning never moves these tests.</summary>
    private static CombatFormula F()
    {
        CombatFormula f = CombatSeed.Formula();
        f.EvasionBase = 50f;
        f.EvasionPerLevel = 10f;
        f.EvasionCap = 30f;
        f.ResistanceCap = 75f;
        f.ResistanceFloor = -100f;
        return f;
    }

    private static AttackerCombat Attacker(uint level = 1, float crit = 0f) => new(level, crit);

    private static DefenderCombat Defender(uint armor = 0, uint evasion = 0, float block = 0f) => new(armor, evasion, block);

    private static DamageParts Physical(double amount) => DamageParts.Of(DamageType.Physical, amount);

    // ---- evasion ----

    [Theory]
    [InlineData(0u, 1u, 0d)]
    [InlineData(20u, 5u, 20d / 120d)]
    [InlineData(10u, 10u, 10d / 160d)]
    [InlineData(40u, 1u, 0.30d)]            // 40/100 capped at 30 %
    public void Evade_by_rating_weighed_against_the_attackers_level(uint evasion, uint level, double chance) =>
        Assert.Equal(chance, HitResolver.EvasionChance(evasion, level, F()), precision: 6);

    // ---- each roll, just under its chance (it procs) and at it (it does not) ----

    [Fact]
    public void Evade_a_roll_just_under_the_chance_and_not_one_at_it()
    {
        // Evasion 20 against a level-1 attacker: 20 / (20 + 50 + 10) = 25 %.
        HitDamage hit = HitResolver.ResolveDamage(Attacker(), Defender(evasion: 20), Physical(100), F(),
            new ScriptedCombatRandom(0.2499));
        Assert.Equal((0u, HitResult.Dodged), (hit.Total, hit.Result));

        hit = HitResolver.ResolveDamage(Attacker(), Defender(evasion: 20), Physical(100), F(),
            new ScriptedCombatRandom(0.25, 0.99, 0.99));
        Assert.Equal((100u, HitResult.None), (hit.Total, hit.Result));
    }

    /// <summary>A crit multiplies by the formula's 1.5 plus the attacker's crit multiplier bonus in points (#1178).</summary>
    [Theory]
    [InlineData(0f, 100d, 150u)]
    [InlineData(50f, 10d, 20u)]
    public void Crit_a_roll_just_under_the_chance_and_not_one_at_it(float critMultiplierBonus, double baseDamage,
        uint crit)
    {
        AttackerCombat a = Attacker(crit: 20f) with { Offence = new OffenceStats { CritMultiplierBonus = critMultiplierBonus } };

        HitDamage hit = HitResolver.ResolveDamage(a, Defender(), Physical(baseDamage), F(),
            new ScriptedCombatRandom(0.99, 0.1999, 0.99));
        Assert.Equal((crit, HitResult.Crit), (hit.Total, hit.Result));

        hit = HitResolver.ResolveDamage(a, Defender(), Physical(baseDamage), F(),
            new ScriptedCombatRandom(0.99, 0.20, 0.99));
        Assert.Equal(((uint)baseDamage, HitResult.None), (hit.Total, hit.Result));
    }

    [Fact]
    public void Block_a_roll_just_under_the_chance_and_not_one_at_it()
    {
        HitDamage hit = HitResolver.ResolveDamage(Attacker(), Defender(block: 5f), Physical(100), F(),
            new ScriptedCombatRandom(0.99, 0.99, 0.0499));
        Assert.Equal((50u, HitResult.Blocked), (hit.Total, hit.Result));

        hit = HitResolver.ResolveDamage(Attacker(), Defender(block: 5f), Physical(100), F(),
            new ScriptedCombatRandom(0.99, 0.99, 0.05));
        Assert.Equal((100u, HitResult.None), (hit.Total, hit.Result));
    }

    // ---- the caps ----

    [Fact]
    public void Cap_evasion_at_the_formulas_evasion_cap()
    {
        // Evasion 240 against a level-1 attacker would be 240 / 300 = 80 %; the cap holds it at 30 %.
        HitDamage hit = HitResolver.ResolveDamage(Attacker(), Defender(evasion: 240), Physical(100), F(),
            new ScriptedCombatRandom(0.35, 0.99, 0.99));
        Assert.Equal((100u, HitResult.None), (hit.Total, hit.Result));

        hit = HitResolver.ResolveDamage(Attacker(), Defender(evasion: 240), Physical(100), F(), new ScriptedCombatRandom(0.2999));
        Assert.Equal(HitResult.Dodged, hit.Result);
    }

    [Fact]
    public void Cap_crit_at_the_formulas_crit_cap()
    {
        HitDamage hit = HitResolver.ResolveDamage(Attacker(crit: 90f), Defender(), Physical(100), F(),
            new ScriptedCombatRandom(0.99, 0.55, 0.99));
        Assert.Equal(HitResult.None, hit.Result);
    }

    [Fact]
    public void Cap_block_at_the_formulas_block_cap()
    {
        HitDamage hit = HitResolver.ResolveDamage(Attacker(), Defender(block: 90f), Physical(100), F(),
            new ScriptedCombatRandom(0.99, 0.99, 0.55));
        Assert.Equal(HitResult.None, hit.Result);
    }

    // ---- the order ----

    [Fact]
    public void Stop_at_an_evasion_and_draw_nothing_after_it()
    {
        var rng = new ScriptedCombatRandom(0.0);

        HitDamage hit = HitResolver.ResolveDamage(Attacker(crit: 50f), Defender(armor: 24, evasion: 20, block: 50f),
            Physical(100), F(), rng);

        Assert.Equal(new HitDamage(0, HitResult.Dodged, DamageType.Physical, DamageTypeMask.Physical, 0), hit);
        Assert.Equal(1, rng.DoublesDrawn);
    }

    [Fact]
    public void Apply_crit_then_block_then_armour()
    {
        // floor(100 x 1.5 x 0.5 x (1 - 24 / (24 + 50 + 10 x 1))) = floor(75 x 0.7142857) = 53
        HitDamage hit = HitResolver.ResolveDamage(Attacker(level: 1, crit: 50f),
            Defender(armor: 24, block: 50f), Physical(100), F(), new ScriptedCombatRandom(0.99, 0.0, 0.0));

        Assert.Equal(53u, hit.Total);
        Assert.Equal(HitResult.Crit | HitResult.Blocked, hit.Result);
    }

    // ---- armour ----

    [Fact]
    public void Reduce_by_the_formula_at_levels_one_and_ten()
    {
        uint atOne = HitResolver.ResolveDamage(Attacker(level: 1), Defender(armor: 24), Physical(100), F(),
            ScriptedCombatRandom.Plain()).Total;
        uint atTen = HitResolver.ResolveDamage(Attacker(level: 10), Defender(armor: 24), Physical(100), F(),
            ScriptedCombatRandom.Plain()).Total;

        Assert.Equal(71u, atOne);  // floor(100 x (1 - 24 / 84))
        Assert.Equal(86u, atTen);  // floor(100 x (1 - 24 / 174))
    }

    [Fact]
    public void Never_reduce_by_more_than_the_armour_cap()
    {
        uint dmg = HitResolver.ResolveDamage(Attacker(level: 1), Defender(armor: 10000), Physical(100), F(),
            ScriptedCombatRandom.Plain()).Total;

        Assert.Equal(25u, dmg);
    }

    [Fact]
    public void Reduce_nothing_with_no_armour_whatever_the_attackers_level()
    {
        uint dmg = HitResolver.ResolveDamage(Attacker(level: uint.MaxValue), Defender(armor: 0), Physical(100), F(),
            ScriptedCombatRandom.Plain()).Total;

        Assert.Equal(100u, dmg);
    }

    [Fact]
    public void Stay_finite_against_an_attacker_of_the_highest_level()
    {
        uint dmg = HitResolver.ResolveDamage(Attacker(level: uint.MaxValue), Defender(armor: 24), Physical(100), F(),
            ScriptedCombatRandom.Plain()).Total;

        Assert.Equal(99u, dmg);
    }

    // ---- resistances, mixed hits and the ward's share ----

    [Theory]
    [InlineData(0f, 0f, 0f, 1.0d)]
    [InlineData(50f, 0f, 0f, 0.5d)]
    [InlineData(90f, 0f, 0f, 0.25d)]        // capped at 75
    [InlineData(90f, 5f, 0f, 0.20d)]        // cap raised to 80
    [InlineData(90f, 20f, 0f, 0.10d)]       // 95 stopped at the 90 ceiling
    [InlineData(75f, 0f, 25f, 0.5d)]        // penetration after the cap
    [InlineData(-50f, 0f, 0f, 1.5d)]        // negative: extra damage
    [InlineData(-300f, 0f, 0f, 2.0d)]       // stopped at the -100 floor
    public void Mitigate_each_element_by_its_capped_resistance_less_penetration(float res, float bonus, float pen,
        double multiplier) =>
        Assert.Equal(multiplier, HitResolver.ResistanceMultiplier(res, bonus, pen, F()), precision: 6);

    [Fact]
    public void Mitigate_each_part_of_a_mixed_hit_on_its_own_and_report_the_largest()
    {
        var hit = DamageParts.Of(DamageType.Physical, 60);
        hit.Set(DamageType.Cold, 40);
        var d = new DefenderCombat(Armor: 20, Evasion: 0, BlockPct: 0, new Resistances(0, 50, 0, 0)); // armour 20 at level 1: 25 %

        HitDamage r = HitResolver.ResolveDamage(Attacker(level: 1), d, hit, F(),
            new ScriptedCombatRandom().Doubles(0.99, 0.99, 0.99));

        Assert.Equal(new HitDamage(65, HitResult.None, DamageType.Physical, DamageTypeMask.Physical | DamageTypeMask.Cold, 65), r);
    }

    [Theory]
    [InlineData(DamageType.Fire, 100d, 0d, 100u)]
    [InlineData(DamageType.Blight, 100d, 0d, 0u)]
    [InlineData(DamageType.Physical, 50d, 50d, 50u)]       // half blight: half absorbable
    public void Leave_blight_out_of_what_a_ward_may_absorb(DamageType first, double firstAmount, double blight, uint absorbable)
    {
        var hit = DamageParts.Of(first, firstAmount);
        hit.Set(DamageType.Blight, hit.Get(DamageType.Blight) + blight);

        HitDamage r = HitResolver.ResolveDamage(Attacker(), Defender(), hit, F(), ScriptedCombatRandom.Plain());

        Assert.Equal(absorbable, r.Absorbable);
    }

    [Theory]
    [InlineData(DamageType.Physical, false, true)]
    [InlineData(DamageType.Physical, true, false)]
    [InlineData(DamageType.Fire, true, true)]
    [InlineData(DamageType.Blight, false, false)]
    [InlineData(DamageType.Blight, true, false)]
    public void Let_the_ward_take_everything_but_blight_and_bleeds(DamageType type, bool periodic, bool absorbs) =>
        Assert.Equal(absorbs, HitResolver.WardAbsorbs(type, periodic));

    [Fact]
    public void Resolve_a_tick_by_its_type_with_one_draw_and_no_evasion()
    {
        var rng = new ScriptedCombatRandom(0.99);
        var d = new DefenderCombat(Armor: 0, Evasion: 1000, BlockPct: 0, new Resistances(50, 0, 0, 0));

        (double damage, HitResult result) = HitResolver.ResolvePeriodic(Attacker(), d, DamageType.Fire, 20f, F(), rng);

        Assert.Equal((10d, HitResult.None), (damage, result));
        Assert.Equal(1, rng.DoublesDrawn);
    }

    // ---- the floor ----

    [Fact]
    public void Deal_at_least_one_to_a_hit_that_lands()
    {
        HitDamage hit = HitResolver.ResolveDamage(Attacker(), Defender(), Physical(0.5), F(), ScriptedCombatRandom.Plain());

        Assert.Equal((1u, HitResult.None), (hit.Total, hit.Result));
    }

    [Fact]
    public void Deal_at_least_one_even_through_the_armour_cap_and_a_block()
    {
        uint dmg = HitResolver.ResolveDamage(Attacker(), Defender(armor: 10000, block: 50f), Physical(1), F(),
            new ScriptedCombatRandom(0.99, 0.99, 0.0)).Total;

        Assert.Equal(1u, dmg);
    }

    [Fact]
    public void Floor_the_damage()
    {
        uint dmg = HitResolver.ResolveDamage(Attacker(), Defender(), Physical(30.99f), F(), ScriptedCombatRandom.Plain()).Total;

        Assert.Equal(30u, dmg);
    }

    // ---- heals ----

    [Fact]
    public void Crit_a_heal()
    {
        (uint heal, HitResult result) = HitResolver.ResolveHeal(Attacker(crit: 20f), 40f, F(), new ScriptedCombatRandom(0.1));

        Assert.Equal((60u, HitResult.Crit), (heal, result));
    }

    [Fact]
    public void Draw_only_the_crit_roll_for_a_heal_and_never_mitigate_it()
    {
        var rng = new ScriptedCombatRandom(0.99);

        (uint heal, HitResult result) = HitResolver.ResolveHeal(Attacker(crit: 20f), 40.7f, F(), rng);

        Assert.Equal((40u, HitResult.None), (heal, result));
        Assert.Equal(1, rng.DoublesDrawn);
    }
}
