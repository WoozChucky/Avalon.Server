using System.Globalization;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Combat;
using Avalon.World.Public.Abilities;

namespace Avalon.Combat.UnitTests;

/// <summary>The aura rules the world server's catalog, its aura system and the balance simulator share.</summary>
public class AuraRulesShould
{
    private static AuraTemplate Bleed() => new()
    {
        Id = new AuraId(1),
        Name = "Bleed",
        Icon = "bleed",
        Kind = AuraKind.Harmful,
        DurationMs = 12000,
        TickIntervalMs = 3000,
        PeriodicKind = AuraPeriodicKind.Damage,
        PeriodicBase = 12f,
        PeriodicEffectiveness = 50f,
        Stacking = AuraStacking.Stack,
        MaxStacks = 3,
    };

    [Fact]
    public void Accept_a_valid_damage_over_time() => Assert.Null(AuraRules.Problem(Bleed()));

    /// <summary>Control is flags: one aura may grant two states, each with its chances to break early.</summary>
    [Fact]
    public void Accept_a_harmful_crowd_control_with_its_break_chances()
    {
        AuraTemplate stun = Bleed();
        stun.PeriodicKind = AuraPeriodicKind.None;
        stun.TickIntervalMs = 0;
        stun.Control = CrowdControl.Stun | CrowdControl.Root;
        stun.BreakOnDamagePct = 10f;
        stun.BreakPerSecondPct = 0.5f;

        Assert.Null(AuraRules.Problem(stun));
    }

    public static TheoryData<string, Action<AuraTemplate>> Refusals() => new()
    {
        { "unknown kind 0", t => t.Kind = 0 },
        { "unknown periodic kind 9", t => t.PeriodicKind = (AuraPeriodicKind)9 },
        { "unknown stacking 0", t => t.Stacking = 0 },
        { "DurationMs must be above 0", t => { t.DurationMs = 0; t.TickIntervalMs = 0; t.PeriodicKind = AuraPeriodicKind.None; } },
        { "a periodic aura needs a TickIntervalMs above 0", t => t.TickIntervalMs = 0 },
        { "TickIntervalMs 13000 is longer than DurationMs 12000", t => t.TickIntervalMs = 13000 },
        { "MaxStacks must be 1 or more", t => t.MaxStacks = 0 },
        { "MaxStacks must be 1 or more", t => { t.Stacking = AuraStacking.Refresh; t.MaxStacks = 0; } },
        { "MaxStacks must be 1 or more", t => { t.Stacking = AuraStacking.Independent; t.MaxStacks = 0; } },
        { "PeriodicBase -1 is not a finite value of 0 or more", t => t.PeriodicBase = -1f },
        { "PeriodicEffectiveness NaN is not a finite value of 0 or more", t => t.PeriodicEffectiveness = float.NaN },
        { "a harmful aura cannot heal", t => t.PeriodicKind = AuraPeriodicKind.Heal },
        { "a helpful aura cannot deal damage", t => t.Kind = AuraKind.Helpful },
        { "a helpful aura cannot stun, root or silence", t =>
            {
                t.Kind = AuraKind.Helpful;
                t.PeriodicKind = AuraPeriodicKind.None;
                t.Control = CrowdControl.Stun;
            } },
        { "unknown control 8", t => t.Control = (CrowdControl)8 },
        { "BreakOnDamagePct -1 is not a finite percentage from 0 to 100", t => t.BreakOnDamagePct = -1f },
        { "BreakOnDamagePct 101 is not a finite percentage from 0 to 100", t => t.BreakOnDamagePct = 101f },
        { "BreakPerSecondPct NaN is not a finite percentage from 0 to 100", t => t.BreakPerSecondPct = float.NaN },
        { FormattableString.Invariant($"BreakPerSecondPct {float.PositiveInfinity} is not a finite percentage from 0 to 100"), t => t.BreakPerSecondPct = float.PositiveInfinity },
        { "unknown stat 0", t => t.Modifiers.Add(new AuraStatModifier { Stat = 0, Kind = AuraModifierKind.Flat, Value = 1f }) },
        { "unknown modifier kind 0 on Armor", t => t.Modifiers.Add(new AuraStatModifier { Stat = AuraStat.Armor, Value = 1f }) },
        { FormattableString.Invariant($"Armor modifier {float.PositiveInfinity} is not finite"), t => t.Modifiers.Add(new AuraStatModifier { Stat = AuraStat.Armor, Kind = AuraModifierKind.Flat, Value = float.PositiveInfinity }) },
        { "Armor is modified twice", t =>
            {
                t.Modifiers.Add(new AuraStatModifier { Stat = AuraStat.Armor, Kind = AuraModifierKind.Flat, Value = 1f });
                t.Modifiers.Add(new AuraStatModifier { Stat = AuraStat.Armor, Kind = AuraModifierKind.Increased, Value = 5f });
            } },
    };

    [Fact]
    public void Write_its_numbers_in_the_invariant_culture_whatever_the_current_one()
    {
        CultureInfo before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-PT");
        try
        {
            AuraTemplate t = Bleed();
            t.PeriodicBase = -1.5f;
            t.Modifiers.Add(new AuraStatModifier { Stat = AuraStat.Armor, Kind = AuraModifierKind.Flat, Value = float.NaN });

            Assert.Equal("PeriodicBase -1.5 is not a finite value of 0 or more", AuraRules.Problem(t));

            t.PeriodicBase = 0f;
            Assert.Equal("Armor modifier NaN is not finite", AuraRules.Problem(t));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Theory]
    [MemberData(nameof(Refusals))]
    public void Refuse_a_row_the_world_could_not_run(string reason, Action<AuraTemplate> breakIt)
    {
        AuraTemplate t = Bleed();
        breakIt(t);

        Assert.Equal(reason, AuraRules.Problem(t));
    }

    /// <summary>
    /// An Increased or a More at or below -100 would take the whole stat; the thirteen Increased keys are percentages
    /// already, so they take Flat ("+X % increased") or More ("X % more"), never Increased.
    /// </summary>
    [Theory]
    [InlineData(AuraStat.Armor, AuraModifierKind.More, -100f, "Armor modifier -100 % would take the whole stat")]
    [InlineData(AuraStat.Armor, AuraModifierKind.Increased, -100f, "Armor modifier -100 % would take the whole stat")]
    [InlineData(AuraStat.IncreasedFireDamage, AuraModifierKind.Increased, 10f,
        "IncreasedFireDamage is already a percentage: it takes Flat or More, never Increased")]
    [InlineData(AuraStat.IncreasedFireDamage, AuraModifierKind.More, 20f, null)]
    public void Refuse_a_modifier_that_does_not_fit_its_stat(AuraStat stat, AuraModifierKind kind, float value,
        string? expected)
    {
        AuraTemplate t = Bleed();
        t.Modifiers.Add(new AuraStatModifier { Stat = stat, Kind = kind, Value = value });

        Assert.Equal(expected, AuraRules.Problem(t));
    }

    [Theory]
    [InlineData(12000u, 3000u, 4)]
    [InlineData(10000u, 3000u, 3)]
    [InlineData(2000u, 3000u, 1)]   // refused by Problem, but a periodic row never counts 0 ticks
    [InlineData(6000u, 0u, 0)]
    public void Count_the_ticks_of_a_duration(uint duration, uint interval, int expected) =>
        Assert.Equal(expected, AuraRules.TickCount(duration, interval));

    /// <summary>A weapon Attack, the source of a damage-over-time aura (#1178).</summary>
    private static readonly AbilityMetadata s_rend = new()
    {
        Name = "Rend",
        Tags = AbilityTags.Attack | AbilityTags.Melee,
        DamageType = DamageType.Physical,
        DamageEffectiveness = 200f,
    };

    /// <summary>
    /// (12 + 50 % of a 10 roll) x 2 for 100 % increased physical attack damage = 34, split over 4 ticks (#1178); the
    /// caster's crit chance, level and crit multiplier bonus, and the ticks' type, go with it, and a tick resolves against
    /// those alone.
    /// </summary>
    [Fact]
    public void Snapshot_the_total_from_the_casters_offence_and_split_it_over_the_ticks()
    {
        OffenceStats offence = default;
        offence.AddIncreased(OffenceStat.PhysicalAttack, 100f);
        offence.CritMultiplierBonus = 25f;
        var caster = new AttackerCombat(3, 7.5f, new WeaponDamage(10, 10, DamageType.Physical, 0, 0, null, 800), offence);

        AuraSnapshot snapshot = AuraRules.Snapshot(Bleed(), caster, s_rend, DamageType.Physical, new FixedRoll(10));

        Assert.Equal(new AuraSnapshot(8.5f, 7.5f, 3, DamageType.Physical, 25f), snapshot);
        Assert.Equal(new AttackerCombat(3, 7.5f, Offence: new OffenceStats { CritMultiplierBonus = 25f }),
            snapshot.Attacker);
    }

    [Fact]
    public void Snapshot_nothing_to_split_for_a_stat_aura()
    {
        AuraTemplate fortified = Bleed();
        fortified.PeriodicKind = AuraPeriodicKind.None;
        fortified.TickIntervalMs = 0;

        Assert.Equal(0f, AuraRules.Snapshot(fortified, new AttackerCombat(1, 5f), s_rend, DamageType.Physical,
            NoRoll.Instance).PerTickPerStack);
    }

    /// <summary>
    /// A creature's poison keeps 3 + a whole roll of its natural 5..9 (7 here) = 10, over 3 ticks: a creature has no
    /// offence of its own. The roll is drawn once, over the whole range, both ends included.
    /// </summary>
    [Fact]
    public void Add_a_whole_roll_of_a_creatures_natural_range_to_its_poison()
    {
        AuraTemplate poison = Bleed();
        poison.DurationMs = 9000;
        poison.PeriodicBase = 3f;
        poison.PeriodicEffectiveness = 100f;
        var spit = new AbilityMetadata { Name = "Venom Spit", Tags = AbilityTags.Attack | AbilityTags.Projectile };
        var rng = new FixedRoll(7);

        AuraSnapshot snapshot = AuraRules.Snapshot(poison,
            new AttackerCombat(4, 5f, new WeaponDamage(5, 9, null, 0, 0, null, 2250)), spit, DamageType.Physical, rng);

        Assert.Equal(10f / 3f, snapshot.PerTickPerStack, precision: 4);
        Assert.Equal([(5L, 9L)], rng.Draws);
    }

    /// <summary>No source (an item's or a script's aura), no ticks, or no effectiveness: no roll, PeriodicBase alone.</summary>
    [Fact]
    public void Draw_no_roll_without_a_source_ticks_or_an_effectiveness()
    {
        var caster = new AttackerCombat(1, 0f, new WeaponDamage(4, 7, DamageType.Physical, 0, 0, null, 800));
        AuraTemplate statOnly = Bleed();
        statOnly.PeriodicKind = AuraPeriodicKind.None;
        statOnly.TickIntervalMs = 0;
        AuraTemplate noEffectiveness = Bleed();
        noEffectiveness.PeriodicEffectiveness = 0f;

        // The assertion is that none of these snapshots draws: NoRoll throws on any draw.
        float sourceless = AuraRules.Snapshot(Bleed(), caster, null, DamageType.Physical, NoRoll.Instance).PerTickPerStack;
        AuraRules.Snapshot(statOnly, caster, s_rend, DamageType.Physical, NoRoll.Instance);
        AuraRules.Snapshot(noEffectiveness, caster, s_rend, DamageType.Physical, NoRoll.Instance);

        Assert.Equal(3f, sourceless);
    }

    /// <summary>A random that fails the test on any draw: the snapshot must not roll.</summary>
    private sealed class NoRoll : ICombatRandom
    {
        public static readonly NoRoll Instance = new();

        public double NextDouble() => throw new InvalidOperationException("the snapshot rolled a chance");

        public long NextInt64(long minInclusive, long maxInclusive) =>
            throw new InvalidOperationException("the snapshot rolled base damage");
    }

    /// <summary>Answers every base damage roll with one value and records the ranges asked for.</summary>
    private sealed class FixedRoll(long value) : ICombatRandom
    {
        public List<(long Min, long Max)> Draws { get; } = [];

        public double NextDouble() => throw new InvalidOperationException("the snapshot rolled a chance");

        public long NextInt64(long minInclusive, long maxInclusive)
        {
            Draws.Add((minInclusive, maxInclusive));
            return value;
        }
    }

    /// <summary>0.4 a tick: nothing until the carry reaches a whole point, then the point; no minimum of 1.</summary>
    [Fact]
    public void Give_nothing_until_the_carry_reaches_a_whole_point()
    {
        double carry = 0d;

        Assert.Equal(0u, AuraRules.TakeTick(0.4, ref carry, lastTick: false));
        Assert.Equal(0u, AuraRules.TakeTick(0.4, ref carry, lastTick: false));
        Assert.Equal(1u, AuraRules.TakeTick(0.4, ref carry, lastTick: false));
        Assert.Equal(0.2, carry, precision: 6);
    }

    [Theory]
    [InlineData(0.5, 1u)]
    [InlineData(0.49, 0u)]
    [InlineData(0.0, 0u)]
    public void Round_the_leftover_on_the_last_tick_and_carry_nothing_on(double leftover, uint expected)
    {
        double carry = leftover;

        Assert.Equal(expected, AuraRules.TakeTick(0d, ref carry, lastTick: true));
        Assert.Equal(0d, carry);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-3d)]
    public void Count_an_unreadable_or_negative_tick_as_nothing(double amount)
    {
        double carry = 0.25;

        Assert.Equal(0u, AuraRules.TakeTick(amount, ref carry, lastTick: false));
        Assert.Equal(0.25, carry);
    }

    [Theory]
    [InlineData(AuraStacking.Stack, 1u, 3u, 2u)]
    [InlineData(AuraStacking.Stack, 3u, 3u, 3u)]
    [InlineData(AuraStacking.Stack, 1u, 0u, 1u)]   // a cap of 0 is refused by Problem, and counts as 1 here
    [InlineData(AuraStacking.Refresh, 1u, 3u, 1u)]
    [InlineData(AuraStacking.Independent, 1u, 3u, 1u)]
    public void Add_a_stack_only_to_a_stacking_aura_and_stop_at_its_cap(AuraStacking stacking, uint current, uint max,
        uint expected) => Assert.Equal(expected, AuraRules.NextStacks(stacking, current, max));

    [Fact]
    public void Key_only_independent_auras_by_caster()
    {
        Assert.True(AuraRules.KeysByCaster(AuraStacking.Independent));
        Assert.False(AuraRules.KeysByCaster(AuraStacking.Stack));
        Assert.False(AuraRules.KeysByCaster(AuraStacking.Refresh));
    }

    [Theory]
    [InlineData(AuraKind.Harmful, AbilityAffects.Hostile, true)]
    [InlineData(AuraKind.Helpful, AbilityAffects.Ally, true)]
    [InlineData(AuraKind.Helpful, AbilityAffects.Hostile, false)]
    [InlineData(AuraKind.Harmful, AbilityAffects.Ally, false)]
    public void Fit_a_harmful_aura_to_hostile_abilities_and_a_helpful_one_to_ally_abilities(AuraKind kind,
        AbilityAffects affects, bool fits) => Assert.Equal(fits, AuraRules.Fits(kind, affects));

    [Fact]
    public void Accept_an_ability_that_names_no_aura() =>
        Assert.Null(AuraRules.LinkProblem(AbilityAffects.Hostile, null, _ => throw new InvalidOperationException("not asked")));

    [Fact]
    public void Accept_an_ability_whose_aura_fits() =>
        Assert.Null(AuraRules.LinkProblem(AbilityAffects.Hostile, new AuraId(1), _ => Bleed()));

    [Fact]
    public void Refuse_an_ability_naming_an_aura_that_is_not_there() =>
        Assert.Equal("names aura 999, which is missing or refused",
            AuraRules.LinkProblem(AbilityAffects.Hostile, new AuraId(999), _ => null));

    [Fact]
    public void Refuse_a_harmful_aura_on_an_ally_ability() =>
        Assert.Equal("its aura 1 'Bleed' is Harmful, which an Ally ability cannot apply",
            AuraRules.LinkProblem(AbilityAffects.Ally, new AuraId(1), _ => Bleed()));

    [Fact]
    public void Refuse_a_helpful_aura_on_a_hostile_ability()
    {
        AuraTemplate renew = Bleed();
        renew.Name = "Renew";
        renew.Kind = AuraKind.Helpful;
        renew.PeriodicKind = AuraPeriodicKind.Heal;

        Assert.Equal("its aura 1 'Renew' is Helpful, which a Hostile ability cannot apply",
            AuraRules.LinkProblem(AbilityAffects.Hostile, new AuraId(1), _ => renew));
    }
}
