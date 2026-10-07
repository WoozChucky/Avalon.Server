using Avalon.Balance.Core;
using Avalon.Combat;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Enums;
using Xunit;

namespace Avalon.Balance.UnitTests;

/// <summary>The simulator's auras: ticks on the server's schedule, stats folded the server's way, gone at death.</summary>
public class AuraSimulationShould
{
    private static AuraTemplate Bleed() => new()
    {
        Id = new AuraId(9901),
        Name = "Test Bleed",
        Icon = "bleed",
        Kind = AuraKind.Harmful,
        DurationMs = 12000,
        TickIntervalMs = 3000,
        PeriodicKind = AuraPeriodicKind.Damage,
        PeriodicBase = 12f,
        ScalingStat = ScalingStat.Attack,
        ScalingCoefficient = 0.25f,
        Stacking = AuraStacking.Stack,
        MaxStacks = 3,
    };

    private static AuraTemplate Ward() => new()
    {
        Id = new AuraId(9905),
        Name = "Test Ward",
        Icon = "ward",
        Kind = AuraKind.Helpful,
        DurationMs = 30000,
        Stacking = AuraStacking.Refresh,
        MaxStacks = 1,
    };

    private static AbilityTemplate Rend() => new()
    {
        Id = new AbilityId(9203),
        Name = "Test Rend",
        ScriptName = AbilityRules.ConeScript,
        Shape = AbilityShape.Cone,
        AimMode = AbilityAimMode.Movement,
        Reach = 2.5f,
        ArcDegrees = 90f,
        Cooldown = 6000,
        Effects = SpellEffect.Damage,
        EffectValue = 8,
        ScalingStat = ScalingStat.Attack,
        ScalingCoefficient = 0.2f,
        BaseDamageCoefficient = 0.5f,
        AllowedClasses = [CharacterClass.Warrior],
        AuraId = new AuraId(9901),
        ThreatMultiplier = 1f,
    };

    private static BalanceData Data(AuraTemplate? bleed = null, Action<SeedTables>? change = null)
    {
        SeedTables seed = TestData.Seed();
        change?.Invoke(seed);
        seed.AuraTemplates.Add(bleed ?? Bleed());
        seed.AuraTemplates.Add(Ward());
        seed.AuraStatModifiers.Add(new AuraStatModifier
        { AuraId = new AuraId(9905), Stat = AuraStat.Armor, Kind = AuraModifierKind.Flat, Value = 50f });
        seed.AbilityTemplates.Add(Rend());
        return BalanceData.From(seed);
    }

    private static (FightSimulator Fight, SimCreature Boar) Fight(BalanceData data, ICombatRandom? rng = null)
    {
        SimPlayer warrior = SimPlayer.Create(data, CharacterClass.Warrior, 3, []);
        warrior.Abilities.Add(new SimAbility(data.Abilities[new AbilityId(9203)]));
        SimCreature boar = SimCreature.Create(data, data.Creature(4), 3, 0);
        // Its base maximum too, as the parity test's server boar has, so a stat aura keeps its health where it is.
        boar.Health = boar.CurrentHealth = boar.BaseMaxHealth = 1_000_000;
        foreach (SimAbility a in boar.Abilities) a.CooldownLeft = 1_000f;
        var fight = new FightSimulator(data.Combat.Formula, warrior, [boar], [new CompiledRotationEntry(9203, [])],
            rng ?? CombatRandom.Steady, data: data);
        return (fight, boar);
    }

    [Fact]
    public void Read_auras_and_their_modifiers_from_the_seed_tables()
    {
        BalanceData data = Data();

        Assert.Equal(AuraStat.Armor, Assert.Single(data.Auras[new AuraId(9905)].Modifiers).Stat);
        Assert.Empty(data.Auras[new AuraId(9901)].Modifiers);
    }

    [Fact]
    public void Refuse_an_ability_whose_aura_is_missing()
    {
        SeedTables seed = TestData.Seed();
        seed.AbilityTemplates.Add(Rend());

        InvalidDataException refused = Assert.Throws<InvalidDataException>(() => BalanceData.From(seed));
        Assert.Contains("names aura 9901, which is missing or refused", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuse_an_aura_the_servers_checks_refuse()
    {
        SeedTables seed = TestData.Seed();
        AuraTemplate bleed = Bleed();
        bleed.TickIntervalMs = 0;
        seed.AuraTemplates.Add(bleed);

        InvalidDataException refused = Assert.Throws<InvalidDataException>(() => BalanceData.From(seed));
        Assert.Contains("aura 9901 'Test Bleed': a periodic aura needs a TickIntervalMs above 0", refused.Message,
            StringComparison.Ordinal);
    }

    /// <summary>Rend every 6 s: its bleed stacks and ticks on the server's schedule, and each tick is counted under its name.</summary>
    [Fact]
    public void Tick_an_abilitys_bleed_on_the_servers_schedule()
    {
        (FightSimulator fight, _) = Fight(Data());

        for (int tick = 0; tick < 60 * 3; tick++) fight.Tick();
        Assert.False(fight.Result().DamageDealt.ContainsKey("Test Bleed"));   // the first tick is at 3 s

        fight.Tick();
        Assert.True(fight.Result().DamageDealt["Test Bleed"] > 0);
    }

    /// <summary>
    /// One application of a bleed worth 23.5 over 4 ticks (no armour, no scaling, no crit): the carry gives 5, 6, 6, 7,
    /// the last tick rounding what is left, so the ticks add up to the total rounded.
    /// </summary>
    [Fact]
    public void Carry_the_fraction_of_a_point_from_tick_to_tick()
    {
        AuraTemplate bleed = Bleed();
        bleed.PeriodicBase = 23.5f;
        bleed.ScalingCoefficient = 0f;
        BalanceData data = Data(bleed, seed => seed.CreatureBaseStats.ForEach(row => row.Armor = 0));
        (FightSimulator fight, SimCreature boar) = Fight(data);
        Assert.Equal(0u, boar.Defence.Armor);
        var ticks = new List<uint>();
        uint health = 0;
        bool cast = false;

        for (int tick = 0; tick <= 60 * 12; tick++)
        {
            fight.Tick();
            if (!cast)
            {
                // The cast's own hit lands on tick 0; the bleed's ticks are counted from the health after it, and Rend is
                // not cast again within the bleed.
                cast = true;
                health = boar.CurrentHealth;
                fight.Player.Ability(9203).CooldownLeft = 1_000f;
                continue;
            }

            if (boar.CurrentHealth != health)
            {
                ticks.Add(health - boar.CurrentHealth);
                health = boar.CurrentHealth;
            }
        }

        Assert.Equal([5u, 6u, 6u, 7u], ticks);
        Assert.Empty(boar.Auras);
    }

    /// <summary>A second Rend within the bleed adds a stack and starts its time over, with a fresh carry; the cap holds.</summary>
    [Fact]
    public void Stack_a_bleed_up_to_its_cap_and_start_its_time_over()
    {
        (FightSimulator fight, SimCreature boar) = Fight(Data());

        for (int tick = 0; tick < 60 * 20; tick++) fight.Tick();   // Rend at 0, 6, 12 and 18 s

        SimAura bleed = Assert.Single(boar.Auras);
        Assert.Equal(3u, bleed.Stacks);
        Assert.Equal(FightSimulator.Epoch + TimeSpan.FromSeconds(18 + 12), bleed.Schedule.ExpiresAt);
    }

    [Fact]
    public void Fold_a_stat_aura_into_the_players_defence_and_take_it_back()
    {
        BalanceData data = Data();
        SimPlayer warrior = SimPlayer.Create(data, CharacterClass.Warrior, 3, []);
        uint armour = warrior.Defence.Armor;

        warrior.Auras.Add(new SimAura(data.Auras[new AuraId(9905)], warrior, 1, default,
            AuraSchedule.Start(FightSimulator.Epoch, 30000, 0), 0));
        warrior.ApplyAuraStats(data);
        Assert.Equal(armour + 50, warrior.Defence.Armor);

        warrior.Auras.Clear();
        warrior.ApplyAuraStats(data);
        Assert.Equal(armour, warrior.Defence.Armor);
    }

    [Fact]
    public void Fold_a_stat_aura_into_a_creatures_defence_and_health_and_take_it_back()
    {
        BalanceData data = Data();
        SimCreature boar = SimCreature.Create(data, data.Creature(4), 3, 0);
        uint armour = boar.Defence.Armor;
        uint health = boar.Health;
        AuraTemplate ward = Ward();
        ward.Modifiers =
        [
            new AuraStatModifier { AuraId = ward.Id, Stat = AuraStat.Armor, Kind = AuraModifierKind.Flat, Value = 50f },
            new AuraStatModifier { AuraId = ward.Id, Stat = AuraStat.MaxHealth, Kind = AuraModifierKind.Percent, Value = 100f },
        ];

        boar.Auras.Add(new SimAura(ward, boar, 1, default, AuraSchedule.Start(FightSimulator.Epoch, 30000, 0), 0));
        boar.ApplyAuraStats();
        Assert.Equal(armour + 50, boar.Defence.Armor);
        Assert.Equal(health * 2, boar.Health);
        Assert.Equal(health * 2, boar.CurrentHealth);

        boar.Auras.Clear();
        boar.ApplyAuraStats();
        Assert.Equal(armour, boar.Defence.Armor);
        Assert.Equal(health, boar.Health);
        Assert.Equal(health, boar.CurrentHealth);
    }

    /// <summary>
    /// A creature's poison (no damage stats) snapshots 3 + 1.0 x a roll of its natural damage: Steady rolls the low end,
    /// so one tick is (3 + DamageMin) / 3.
    /// </summary>
    [Fact]
    public void Scale_a_creatures_poison_by_a_roll_of_its_natural_damage()
    {
        BalanceData data = Data();
        AuraTemplate poison = Bleed();
        poison.PeriodicBase = 3f;
        poison.ScalingCoefficient = 0f;
        poison.BaseDamageCoefficient = 1f;
        poison.DurationMs = 9000;
        SimCreature boar = SimCreature.Create(data, data.Creature(4), 3, 0);

        AuraSnapshot snapshot = AuraRules.Snapshot(poison, boar.Attack, CombatRandom.Steady);

        Assert.True(boar.Attack.WeaponMin > 0);
        Assert.Equal((3f + boar.Attack.WeaponMin) / 3f, snapshot.PerTickPerStack, precision: 4);
    }

    [Fact]
    public void End_every_aura_on_a_unit_that_dies()
    {
        (FightSimulator fight, SimCreature boar) = Fight(Data());
        fight.Tick();
        Assert.Single(boar.Auras);

        boar.CurrentHealth = 0;
        fight.Tick();

        Assert.Empty(boar.Auras);
    }

    [Fact]
    public void Run_unchanged_without_aura_data()
    {
        BalanceData data = Data();
        SimPlayer warrior = SimPlayer.Create(data, CharacterClass.Warrior, 3, []);
        warrior.Abilities.Add(new SimAbility(data.Abilities[new AbilityId(9203)]));
        SimCreature boar = SimCreature.Create(data, data.Creature(4), 3, 0);
        var fight = new FightSimulator(data.Combat.Formula, warrior, [boar], [new CompiledRotationEntry(9203, [])],
            CombatRandom.Steady);

        for (int tick = 0; tick < 600; tick++) fight.Tick();

        Assert.Empty(boar.Auras);
    }
}
