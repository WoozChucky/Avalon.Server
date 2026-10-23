using Avalon.Combat;
using Avalon.Database.World.Seeding;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Combat;
using Avalon.World.Public.Abilities;

namespace Avalon.Server.World.UnitTests.Combat;

/// <summary>
/// The offence maths (#1178): an attack's weapon roll, a spell's base grown by level, conversion before scaling, each
/// part scaled by its increased sum and its more product, and a damage over time's total fixed at application.
/// </summary>
public class OffenceMathShould
{
    private static readonly WeaponDamage s_weapon = new(10, 20, DamageType.Physical, 0, 0, null, 800);

    private static AttackerCombat Attacker(OffenceStats offence, WeaponDamage weapon = default, uint level = 1) =>
        new(level, 0f, weapon, offence);

    private static OffenceStats Offence(params (OffenceStat Stat, float Increased)[] increased)
    {
        OffenceStats o = default;
        foreach ((OffenceStat stat, float points) in increased) o.AddIncreased(stat, points);
        return o;
    }

    /// <summary>The fixture's attacker: Increased(PhysicalAttack) 50 and Increased(Cold) 100.</summary>
    private static OffenceStats Fixture() => Offence((OffenceStat.PhysicalAttack, 50f), (OffenceStat.Cold, 100f));

    private static AbilityMetadata MeleeAttack(float convertPct = 0f, uint added = 0) => new()
    {
        Tags = AbilityTags.Attack | AbilityTags.Melee,
        DamageType = DamageType.Physical,
        DamageEffectiveness = 150f,
        ConvertToType = DamageType.Cold,
        ConvertPct = convertPct,
        AddedType = DamageType.Blight,
        AddedMin = added,
        AddedMax = added,
    };

    [Theory]
    [InlineData(0f, 0u, 10L, 22.5, 0d, 0d)]      // 10 x 1.5 x 1.5
    [InlineData(50f, 0u, 10L, 11.25, 15d, 0d)]   // converted cold takes cold's 100 %, never physical's 50 %
    [InlineData(100f, 0u, 10L, 0d, 30d, 0d)]
    [InlineData(0f, 4u, 10L, 22.5, 0d, 6d)]      // added blight 4..4: not converted, x 1.5
    public void Build_an_attacks_parts_from_its_weapon_roll_conversion_and_added_damage(float convertPct, uint added,
        long roll, double physical, double cold, double blight)
    {
        ScriptedCombatRandom rng = new ScriptedCombatRandom().Longs(roll, added);

        DamageParts parts = OffenceMath.AttackBase(Attacker(Fixture(), s_weapon), MeleeAttack(convertPct, added), rng);

        Assert.Equal(physical, parts.Get(DamageType.Physical), precision: 9);
        Assert.Equal(cold, parts.Get(DamageType.Cold), precision: 9);
        Assert.Equal(blight, parts.Get(DamageType.Blight), precision: 9);
        Assert.Equal(added > 0 ? [(10L, 20L), (added, added)] : [(10L, 20L)], rng.WeaponRolls);
    }

    [Theory]
    [InlineData(DamageType.Fire, AbilityTags.Spell | AbilityTags.Area, 3.6)]
    [InlineData(DamageType.Fire, AbilityTags.Attack, 2.34)]
    [InlineData(DamageType.Physical, AbilityTags.Attack | AbilityTags.Melee, 2.64)]
    [InlineData(DamageType.Physical, AbilityTags.Spell, 1.68)]   // Damage 10 + Spell 30, never PhysicalAttack: 1.4 x 1.2
    public void Scale_a_part_by_its_increased_sum_and_its_more_product(DamageType type, AbilityTags tags, double scale)
    {
        OffenceStats o = Offence((OffenceStat.Damage, 10f), (OffenceStat.Fire, 20f), (OffenceStat.Spell, 30f),
            (OffenceStat.Area, 40f), (OffenceStat.PhysicalAttack, 50f), (OffenceStat.Melee, 60f));
        o.AddMore(OffenceStat.Damage, 20f);
        o.AddMore(OffenceStat.Fire, 50f);

        Assert.Equal(scale, OffenceMath.Scale(type, tags, o), precision: 6);
    }

    [Fact]
    public void Floor_a_part_at_nothing_when_its_increased_sum_is_below_minus_100()
    {
        AttackerCombat a = Attacker(Offence((OffenceStat.Damage, -150f)), s_weapon);

        DamageParts parts = OffenceMath.AttackBase(a, MeleeAttack(), new ScriptedCombatRandom().Longs(10));
        HitDamage hit = HitResolver.ResolveDamage(a, new DefenderCombat(0, 0, 0f), parts, CombatSeed.Formula(),
            ScriptedCombatRandom.Plain());

        Assert.Equal(0d, parts.Get(DamageType.Physical));
        Assert.Equal(1u, hit.Total);
    }

    [Theory]
    [InlineData(1u, 15d)]    // spell base 10..10, growth 10 %/level, Spell 50: 10 x 1.0 x 1.5
    [InlineData(5u, 21d)]    // 10 x 1.4 x 1.5
    public void Grow_a_spells_base_with_the_casters_level(uint level, double lightning)
    {
        var spell = new AbilityMetadata
        {
            Tags = AbilityTags.Spell,
            DamageType = DamageType.Lightning,
            SpellBaseMin = 10,
            SpellBaseMax = 10,
            SpellBasePerLevelPct = 10f,
        };

        DamageParts parts = OffenceMath.SpellBase(Attacker(Offence((OffenceStat.Spell, 50f)), level: level), spell,
            new ScriptedCombatRandom().Longs(10));

        Assert.Equal(lightning, parts.Get(DamageType.Lightning), precision: 9);
        Assert.Equal(lightning, parts.Total, precision: 9);
    }

    [Theory]
    [InlineData("attack", 34d)]   // weapon 10..10, PeriodicBase 12, PE 50, PhysicalAttack 100: (12 + 5) x 2
    [InlineData("spell", 60d)]    // spell base 30..30, PE 100, Spell 50, DamageOverTime 50: 30 x 2
    [InlineData("heal", 30d)]     // heal base 20..20, PE 100, Healing 50
    [InlineData("none", 12d)]     // no source: PeriodicBase 12
    public void Total_a_damage_over_time_from_its_source_at_application(string source, double total)
    {
        var rng = new ScriptedCombatRandom();
        (AuraTemplate aura, AttackerCombat caster, AbilityMetadata? ability, DamageType type) = source switch
        {
            "attack" => (Aura(AuraPeriodicKind.Damage, 12f, 50f),
                Attacker(Offence((OffenceStat.PhysicalAttack, 100f)), new WeaponDamage(10, 10, DamageType.Physical, 0, 0, null, 800)),
                new AbilityMetadata { Tags = AbilityTags.Attack, DamageType = DamageType.Physical }, DamageType.Physical),
            "spell" => (Aura(AuraPeriodicKind.Damage, 0f, 100f),
                Attacker(Offence((OffenceStat.Spell, 50f), (OffenceStat.DamageOverTime, 50f))),
                new AbilityMetadata
                {
                    Tags = AbilityTags.Spell,
                    DamageType = DamageType.Fire,
                    SpellBaseMin = 30,
                    SpellBaseMax = 30,
                }, DamageType.Fire),
            "heal" => (Aura(AuraPeriodicKind.Heal, 0f, 100f), Attacker(Offence((OffenceStat.Healing, 50f))),
                new AbilityMetadata { Tags = AbilityTags.Heal, SpellBaseMin = 20, SpellBaseMax = 20 }, DamageType.Physical),
            _ => (Aura(AuraPeriodicKind.Damage, 12f, 50f), Attacker(Offence((OffenceStat.DamageOverTime, 100f))),
                (AbilityMetadata?)null, DamageType.Physical),
        };
        if (ability is not null) rng.Longs(source == "attack" ? 10 : (long)ability.SpellBaseMax);

        Assert.Equal(total, OffenceMath.PeriodicTotal(aura, caster, ability, type, rng), precision: 9);
        Assert.Equal(0, rng.LongsLeft);
    }

    private static AuraTemplate Aura(AuraPeriodicKind kind, float periodicBase, float effectiveness) => new()
    {
        PeriodicKind = kind,
        PeriodicBase = periodicBase,
        PeriodicEffectiveness = effectiveness,
    };
}
