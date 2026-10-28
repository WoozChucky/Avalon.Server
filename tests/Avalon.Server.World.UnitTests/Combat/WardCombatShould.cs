using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Combat;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Auras;
using Avalon.World.Characters;
using Avalon.World.Combat;
using Avalon.World.Entities;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;
using Avalon.World.Testing;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Combat;

/// <summary>
/// The ward through the combat service (#1178): it takes a hit before life, except blight, and a burn tick but not a
/// bleed; a hit it takes whole leaves a creature untouched; it empties at death and recharges only after the delay from
/// the revive. A hit's type is its ability's, else the attacker's. No armour and no resistance unless stated.
/// </summary>
public class WardCombatShould
{
    private static readonly AuraId s_aura = new(901);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero));
    private readonly ICombatOutcomes _outcomes = Substitute.For<ICombatOutcomes>();
    private readonly CombatService _combat;

    public WardCombatShould()
    {
        var registry = new EncounterRegistry(new CombatConfig(), _time);
        _combat = new CombatService(new CombatConfig(), registry, outcomes: _outcomes, time: _time,
            random: ScriptedCombatRandom.Plain());
    }

    private CharacterEntity Character(uint maxWard, uint armor = 0)
    {
        CharacterEntity character = TestCharacters.New(1178_101, time: _time);
        character.ApplyStats(new DerivedCharacterStats(MaxHealth: 500, MaxPower: 100, Stamina: 0, Strength: 0, Agility: 0,
            Intellect: 0, Armor: armor, BlockPct: 0f, Evasion: 0, CritPct: 0f,
            MaxWard: maxWard), CurrentValues.EnterWorld, TestCombat.Formula);
        return character;
    }

    private static Creature Boar(DamageType type = DamageType.Physical, uint ward = 0, uint armor = 0) => new()
    {
        Guid = new ObjectGuid(ObjectType.Creature, 1178_901),
        Metadata = Substitute.For<ICreatureMetadata>(),
        Level = 1,
        Health = 100,
        CurrentHealth = 100,
        DamageType = type,
        BaseWard = ward,
        Armor = armor,
    };

    private PeriodicHit Tick(IUnit target, DamageType type, float amount) =>
        new(null, target, s_aura, amount, new AuraSnapshot(0f, 0f, 1, type), AuraSource.None);

    [Theory]
    [InlineData(DamageType.Physical, 20u, 0u)]   // ward 30 takes 30 of 50
    [InlineData(DamageType.Blight, 50u, 30u)]    // blight passes it by
    public void Take_a_hit_on_the_ward_before_life_unless_it_is_blight(DamageType type, uint healthLost, uint wardLeft)
    {
        CharacterEntity character = Character(maxWard: 30);
        Creature boar = Boar(type);

        _combat.ApplyDamage(boar, character, 50);

        Assert.Equal((500u - healthLost, wardLeft), (character.CurrentHealth, character.Ward));
        // The character is told the whole hit, ward included.
        _outcomes.Received(1).CharacterDamaged(character, boar, 50u, null,
            new HitReport(HitResult.None, type, DamageTypes.MaskOf(type), 30u - wardLeft));
    }

    [Fact]
    public void Leave_a_creature_alive_and_untouched_when_its_ward_takes_the_whole_hit()
    {
        Creature boar = Boar(ward: 100);
        var script = new HitRecordingScript(boar);
        boar.Script = script;
        var inFlight = new List<HitReport>();
        _outcomes.HitInFlight = Arg.Do<HitReport>(inFlight.Add);
        CharacterEntity character = Character(maxWard: 0);

        _combat.ApplyDamage(character, boar, 40);

        Assert.Equal((100u, 60u), (boar.CurrentHealth, boar.Ward));
        Assert.Equal([0u], script.Hits);   // the life part alone
        Assert.Equal([new HitReport(HitResult.None, DamageType.Physical, DamageTypeMask.Physical, 40u), HitReport.None],
            inFlight);
        _outcomes.DidNotReceiveWithAnyArgs().CreatureKilled(default!, default);
        _outcomes.Received(1).DamageTaken(boar);
    }

    [Theory]
    [InlineData(DamageType.Physical, 10u, 50u)]  // a bleed tick: health -10, ward stays 50
    [InlineData(DamageType.Fire, 0u, 40u)]       // a burn tick: the ward takes it
    public void Pass_a_bleed_tick_through_the_ward_and_absorb_a_burn_tick(DamageType type, uint healthLost, uint wardLeft)
    {
        CharacterEntity character = Character(maxWard: 50);

        uint dealt = _combat.ApplyPeriodicDamage(Tick(character, type, 10f));

        Assert.Equal(10u, dealt);
        Assert.Equal((500u - healthLost, wardLeft), (character.CurrentHealth, character.Ward));
        _outcomes.Received(1).PeriodicTick(null, character, 10u, s_aura,
            new HitReport(HitResult.None, type, DamageTypes.MaskOf(type), 50u - wardLeft), false);
    }

    [Fact]
    public void Empty_the_ward_at_death_and_refill_it_only_after_the_delay_from_the_revive()
    {
        CharacterEntity character = Character(maxWard: 50);
        character.Spells.Load([]);   // its update counts the cooldowns down
        var tick = TimeSpan.FromSeconds(1d / 60d);

        _combat.ApplyDamage(Boar(DamageType.Blight), character, 1000);   // blight never meets the ward
        Assert.True(character.IsDead);
        Assert.Equal(0u, character.Ward);

        _time.Advance(TimeSpan.FromSeconds(10));
        character.Update(tick);
        Assert.Equal(0u, character.Ward);

        character.Revive();
        _time.Advance(TimeSpan.FromSeconds(3.9));
        character.Update(tick);
        Assert.Equal(0u, character.Ward);

        // 4 s of delay, then 2 s from empty to full: one second of refill is half.
        _time.Advance(TimeSpan.FromSeconds(1.1));
        character.Update(tick);
        Assert.Equal(25u, character.Ward);
    }

    /// <summary>A hit's type is its ability's (R7): a Fire ability passes armour 60 that would halve a physical 20.</summary>
    [Fact]
    public void Type_an_abilitys_hit_by_the_ability()
    {
        Creature boar = Boar(armor: 60);
        var script = new HitRecordingScript(boar);
        boar.Script = script;
        IAbility fireball = Substitute.For<IAbility>();
        fireball.Metadata.Returns(new AbilityMetadata
        {
            Name = "Fireball",
            ScriptName = "x",
            DamageType = DamageType.Fire,
            Tags = AbilityTags.Spell,
            SpellBaseMin = 20,
            SpellBaseMax = 20,
        });

        var combat = new CombatService(new CombatConfig(), new EncounterRegistry(new CombatConfig(), _time), outcomes: _outcomes,
            time: _time, random: ScriptedCombatRandom.Plain().Longs(20));

        combat.ApplyDamage(Character(maxWard: 0), boar, fireball);

        Assert.Equal([20u], script.Hits);
    }

    /// <summary>A raw swing takes its creature's template type (R7): a Fire creature's 20 passes armour 60 whole.</summary>
    [Fact]
    public void Type_a_creatures_raw_swing_by_its_template()
    {
        CharacterEntity character = Character(maxWard: 0, armor: 60);
        Creature boar = Boar(DamageType.Fire);

        _combat.ApplyDamage(boar, character, 20);

        Assert.Equal(character.Health - 20u, character.CurrentHealth);
        _outcomes.Received(1).CharacterDamaged(character, boar, 20u, null,
            new HitReport(HitResult.None, DamageType.Fire, DamageTypeMask.Fire, 0u));
    }

    /// <summary>
    /// Unbroken (#1181 Part D) on the hit path: a holder's ward takes blight hits and bleed ticks whole, its flag read per
    /// hit and per tick, and neither allocates once the encounter exists.
    /// </summary>
    [Fact]
    public void Absorb_blight_and_bleeds_for_an_unbroken_holder_without_allocating()
    {
        var clock = new Loot.FixedTimeProvider(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));
        var combat = new CombatService(new CombatConfig(), new EncounterRegistry(new CombatConfig(), clock), time: clock);
        CharacterEntity character = TestCharacters.New(1178_102, time: clock);
        character.ApplyStats(new DerivedCharacterStats(MaxHealth: 500, MaxPower: 100, Stamina: 0, Strength: 0, Agility: 0,
            Intellect: 0, Armor: 0, BlockPct: 0f, Evasion: 0, CritPct: 0f, MaxWard: 1_000_000), CurrentValues.EnterWorld,
            TestCombat.Formula);
        character.Talents.Totals.AddKeystone(KeystoneRule.WardTakesAll);
        Creature rotter = Boar(DamageType.Blight);
        PeriodicHit bleed = Tick(character, DamageType.Physical, 10f);
        double carry = 0d;

        combat.ApplyDamage(rotter, character, 50);
        combat.ApplyPeriodicDamage(bleed, ref carry, lastTick: false);

        long before = ThreadAllocations.BeforeQuietWindow();
        for (int i = 0; i < 100; i++)
        {
            combat.ApplyDamage(rotter, character, 50);
            combat.ApplyPeriodicDamage(bleed, ref carry, lastTick: false);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal((500u, 1_000_000u - (101u * 60u)), (character.CurrentHealth, character.Ward));
    }

    /// <summary>Records the damage each hit hands the script.</summary>
    private sealed class HitRecordingScript(ICreature creature) : AiScript(creature, Substitute.For<ISimulationContext>())
    {
        public List<uint> Hits { get; } = [];

        public override object State { get; set; } = 0;

        public override TimeSpan NextUpdateIn => TimeSpan.Zero;

        protected override bool ShouldRun() => false;

        public override void OnHit(IUnit attacker, uint damage) => Hits.Add(damage);
    }
}
