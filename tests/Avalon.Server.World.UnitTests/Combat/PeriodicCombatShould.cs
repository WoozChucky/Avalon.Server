using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.State;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Auras;
using Avalon.World.Combat;
using Avalon.World.Entities;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Units;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Combat;

/// <summary>
/// An aura's ticks through the combat service: crit and armour from the snapshot, threat and kill credit to a caster
/// still present, credit to nobody otherwise, every tick reported to the instance with its aura, and the fraction of a
/// point carried from tick to tick.
/// </summary>
public class PeriodicCombatShould
{
    private static readonly AuraId Bleed = new(901);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero));
    private readonly ICombatOutcomes _outcomes = Substitute.For<ICombatOutcomes>();
    private readonly EncounterRegistry _registry;
    private readonly CombatService _combat;

    public PeriodicCombatShould()
    {
        _registry = new EncounterRegistry(new CombatConfig(), _time);
        _combat = new CombatService(new CombatConfig(), _registry, outcomes: _outcomes, time: _time,
            random: ScriptedCombatRandom.Plain());
    }

    private static Creature Boar(uint health = 100, uint armor = 0) => new()
    {
        Guid = new ObjectGuid(ObjectType.Creature, 908_901), Level = 1, Health = health, CurrentHealth = health, Armor = armor,
    };

    private static CharacterEntity Warrior(uint id = 908_101)
    {
        CharacterEntity warrior = TestCharacters.New(id);
        warrior.Health = 500;
        warrior.CurrentHealth = 500;
        return warrior;
    }

    private static PeriodicHit Tick(IUnit? caster, IUnit target, float amount = 20f, float threat = 1f) =>
        new(caster, target, Bleed, amount, new AuraSnapshot(0f, 0f, 1), AuraSource.None with { ThreatMultiplier = threat });

    /// <summary>Armour 60 against level 1 takes half: 20 becomes 10.</summary>
    [Fact]
    public void Take_a_ticks_damage_after_armour_and_report_it_with_its_aura()
    {
        Creature boar = Boar(armor: 60);
        CharacterEntity warrior = Warrior();

        uint dealt = _combat.ApplyPeriodicDamage(Tick(warrior, boar));

        Assert.Equal(10u, dealt);
        Assert.Equal(90u, boar.CurrentHealth);
        _outcomes.Received(1).PeriodicTick(warrior, boar, 10u, Bleed, HitResult.None, false);
    }

    [Fact]
    public void Give_a_present_caster_threat_by_its_abilitys_multiplier_and_engage_the_creature()
    {
        Creature boar = Boar();
        var script = new RecordingAiScript(boar);
        boar.Script = script;
        CharacterEntity warrior = Warrior();

        _combat.ApplyPeriodicDamage(Tick(warrior, boar, amount: 20f, threat: 1.5f));

        float seed = new CombatConfig().InitialThreatSeed;
        Assert.Equal(seed + 20f * 1.5f * 2.0f, _combat.GetEncounterFor(boar)!.GetThreatList(boar)[warrior], precision: 3);
        Assert.Equal([warrior], script.Attackers);
        Assert.True(warrior.IsInCombat);
    }

    /// <summary>A tick from a caster that is gone kills, and nobody is credited.</summary>
    [Fact]
    public void Kill_with_a_tick_from_nobody_and_credit_nobody()
    {
        Creature boar = Boar(health: 15);

        _combat.ApplyPeriodicDamage(Tick(caster: null, boar));

        Assert.Equal(0u, boar.CurrentHealth);
        _outcomes.Received(1).CreatureKilled(boar, null);
        _outcomes.Received(1).UnitDied(boar);
        Assert.Null(_combat.GetEncounterFor(boar));
    }

    [Fact]
    public void Kill_with_a_tick_and_credit_the_present_caster()
    {
        Creature boar = Boar(health: 15);
        CharacterEntity warrior = Warrior();

        _combat.ApplyPeriodicDamage(Tick(warrior, boar));

        _outcomes.Received(1).CreatureKilled(boar, warrior);
    }

    [Fact]
    public void Pass_over_a_target_that_ignores_hits()
    {
        Creature innkeeper = Boar();
        innkeeper.Invulnerable = true;

        Assert.Equal(0u, _combat.ApplyPeriodicDamage(Tick(Warrior(), innkeeper)));

        Assert.Equal(100u, innkeeper.CurrentHealth);
        _outcomes.DidNotReceiveWithAnyArgs().PeriodicTick(default, default!, default, default!, default, default);
    }

    /// <summary>A Fury character gains its share of the health a tick took, as from any hit (#526).</summary>
    [Fact]
    public void Hurt_a_character_and_grant_fury_from_the_health_lost()
    {
        CharacterEntity warrior = Warrior();
        warrior.PowerType = PowerType.Fury;
        warrior.Power = 100;
        warrior.CurrentPower = 0;

        _combat.ApplyPeriodicDamage(Tick(caster: null, warrior, amount: 50f));

        Assert.Equal(450u, warrior.CurrentHealth);
        Assert.Equal(5u, warrior.CurrentPower);   // floor(50 / 500 x 50)
        Assert.True(warrior.IsInCombat);
        _outcomes.Received(1).PeriodicTick(null, warrior, 50u, Bleed, HitResult.None, false);
    }

    [Fact]
    public void Kill_a_character_with_a_tick()
    {
        CharacterEntity warrior = Warrior();

        _combat.ApplyPeriodicDamage(Tick(caster: null, warrior, amount: 600f));

        Assert.True(warrior.IsDead);
        _outcomes.Received(1).UnitDied(warrior);
    }

    [Fact]
    public void Heal_a_tick_and_report_only_what_it_restored()
    {
        CharacterEntity healer = Warrior(908_102);
        CharacterEntity target = Warrior(908_103);
        target.CurrentHealth = 490;

        uint restored = _combat.ApplyPeriodicHeal(Tick(healer, target, amount: 25f));

        Assert.Equal(10u, restored);
        Assert.Equal(500u, target.CurrentHealth);
        _outcomes.Received(1).PeriodicTick(healer, target, 10u, Bleed, HitResult.None, true);
    }

    [Fact]
    public void Heal_nothing_on_the_dead()
    {
        CharacterEntity target = Warrior();
        target.CurrentHealth = 0;
        target.IsDead = true;

        Assert.Equal(0u, _combat.ApplyPeriodicHeal(Tick(caster: null, target)));
    }

    /// <summary>
    /// Three ticks of 3.4 deal 3, 3 and 4: each takes its whole points and carries the fraction to the next, and the
    /// last rounds what is left, so the ticks add up to the total rounded (10.2 is 10).
    /// </summary>
    [Fact]
    public void Carry_the_fraction_of_a_point_from_tick_to_tick()
    {
        Creature boar = Boar();
        double carry = 0d;

        uint first = _combat.ApplyPeriodicDamage(Tick(caster: null, boar, amount: 3.4f), ref carry, lastTick: false);
        uint second = _combat.ApplyPeriodicDamage(Tick(caster: null, boar, amount: 3.4f), ref carry, lastTick: false);
        uint last = _combat.ApplyPeriodicDamage(Tick(caster: null, boar, amount: 3.4f), ref carry, lastTick: true);

        Assert.Equal([3u, 3u, 4u], new[] { first, second, last });
        Assert.Equal(90u, boar.CurrentHealth);
        Assert.Equal(0d, carry);
    }

    /// <summary>A tick worth less than a point deals nothing, sends nothing, and keeps its fraction for the next.</summary>
    [Fact]
    public void Deal_and_send_nothing_for_a_tick_below_a_point_and_keep_its_fraction()
    {
        Creature boar = Boar();
        double carry = 0d;

        uint dealt = _combat.ApplyPeriodicDamage(Tick(caster: null, boar, amount: 0.4f), ref carry, lastTick: false);

        Assert.Equal(0u, dealt);
        Assert.Equal(100u, boar.CurrentHealth);
        Assert.Equal(0.4d, carry, precision: 5);
        _outcomes.DidNotReceiveWithAnyArgs().PeriodicTick(default, default!, default, default!, default, default);
    }

    [Fact]
    public void Carry_the_fraction_of_a_heal_and_send_nothing_for_a_tick_below_a_point()
    {
        CharacterEntity target = Warrior();
        target.CurrentHealth = 400;
        double carry = 0d;

        uint first = _combat.ApplyPeriodicHeal(Tick(caster: null, target, amount: 0.6f), ref carry, lastTick: false);
        uint second = _combat.ApplyPeriodicHeal(Tick(caster: null, target, amount: 0.6f), ref carry, lastTick: false);

        Assert.Equal([0u, 1u], new[] { first, second });
        Assert.Equal(401u, target.CurrentHealth);
        _outcomes.Received(1).PeriodicTick(null, target, 1u, Bleed, HitResult.None, true);
    }

    [Fact]
    public void Enter_combat_on_a_harmful_aura_with_no_damage_yet()
    {
        Creature boar = Boar();
        var script = new RecordingAiScript(boar);
        boar.Script = script;
        CharacterEntity warrior = Warrior();

        _combat.EnterAuraCombat(warrior, boar);

        Assert.NotNull(_combat.GetEncounterFor(boar));
        Assert.Equal([warrior], script.Attackers);
        Assert.True(warrior.IsInCombat);
        Assert.Equal(100u, boar.CurrentHealth);
    }

    [Fact]
    public void Answer_how_an_abilitys_hit_went()
    {
        var boar = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, 908_902), Level = 1, Health = 100, CurrentHealth = 100, DodgePct = 30f,
        };
        boar.Script = new RecordingAiScript(boar);
        var dodging = new CombatService(new CombatConfig(), _registry, outcomes: _outcomes, time: _time,
            random: new ScriptedCombatRandom(0.0));
        var ability = Substitute.For<Avalon.World.Public.Abilities.IAbility>();
        ability.Metadata.Returns(new Avalon.World.Public.Abilities.AbilityMetadata { Name = "Cleave" });

        Assert.Equal(HitOutcome.Dodged, dodging.ApplyDamageWithOutcome(Warrior(), boar, 10, ability));
        boar.Invulnerable = true;
        Assert.Equal(HitOutcome.Ignored, dodging.ApplyDamageWithOutcome(Warrior(), boar, 10, ability));
    }
}

/// <summary>A creature script that records who attacked it and takes no damage itself.</summary>
internal sealed class RecordingAiScript(ICreature creature) : Avalon.World.Public.Scripts.AiScript(creature, Substitute.For<Avalon.World.Public.Instances.ISimulationContext>())
{
    public List<IUnit> Attackers { get; } = [];

    public override object State { get; set; } = 0;

    protected override bool ShouldRun() => false;

    public override void OnAttacked(IUnit attacker) => Attackers.Add(attacker);
}
