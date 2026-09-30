using Avalon.Balance.Data;
using Avalon.Balance.Simulation;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.State;
using Avalon.World.Combat;
using Avalon.World.Configuration;
using Avalon.World.Public.Enums;
using Xunit;
using Avalon.Combat;

namespace Avalon.Balance.UnitTests;

public class CombatRulesShould
{
    private static BalanceData Data => TestData.Seeded;

    private static SimPlayer Warrior(ushort level = 1, params ulong[] gear) =>
        SimPlayer.Create(Data, CharacterClass.Warrior, level, gear.Select(Data.Item));

    private static SimCreature Boar(ushort level = 1) => SimCreature.Create(Data, Data.Creature(4), level, 0);

    [Fact]
    public void Start_a_warrior_full_of_health_and_empty_of_fury()
    {
        SimPlayer warrior = Warrior();

        Assert.Equal(240u, warrior.CurrentHealth);
        Assert.Equal(PowerType.Fury, warrior.PowerType);
        Assert.Equal(0u, warrior.CurrentPower);
        Assert.Equal(100u, warrior.Power);
        Assert.Equal([200u, 201u, 202u], warrior.Abilities.Select(a => a.Id));
    }

    [Fact]
    public void Start_a_wizard_full_of_mana_regenerating_from_intellect()
    {
        SimPlayer wizard = SimPlayer.Create(Data, CharacterClass.Wizard, 1, []);

        Assert.Equal(wizard.Power, wizard.CurrentPower);
        Assert.Equal(wizard.Stats.Intellect, wizard.RegenStat);
    }

    [Fact]
    public void Deal_cleave_through_the_real_resolver()
    {
        SimPlayer warrior = Warrior();
        SimCreature boar = Boar(1);   // level 1: armour 0

        (uint damage, _) = CombatRules.Damage(warrior, boar, warrior.Ability(200), Data.Combat.Formula, CombatRandom.Steady);

        Assert.Equal(25u, damage);    // floor(25.8)
    }

    [Fact]
    public void Gain_cleaves_fury_per_creature_damaged_up_to_the_maximum()
    {
        SimPlayer warrior = Warrior();
        SimCreature boar = Boar();

        CombatRules.HitCreature(warrior, boar, 10, warrior.Ability(200));
        Assert.Equal(8u, warrior.CurrentPower);

        warrior.CurrentPower = 97;
        CombatRules.HitCreature(warrior, boar, 10, warrior.Ability(200));
        Assert.Equal(100u, warrior.CurrentPower);
    }

    [Fact]
    public void Gain_no_fury_from_a_dodged_hit()
    {
        SimPlayer warrior = Warrior();

        CombatRules.HitCreature(warrior, Boar(), 0, warrior.Ability(200));

        Assert.Equal(0u, warrior.CurrentPower);
    }

    [Fact]
    public void Gain_fury_from_the_health_a_hit_takes()
    {
        SimPlayer warrior = Warrior();

        uint lost = CombatRules.HitPlayer(warrior, 48);

        Assert.Equal(48u, lost);
        Assert.Equal(Fury.FromDamageTaken(48, 240, 240, GameConfiguration.DefaultFuryFromDamageTaken), warrior.CurrentPower);
        Assert.Equal(10u, warrior.CurrentPower);   // floor(48 / 240 x 50)
    }

    [Fact]
    public void Gain_nothing_from_the_hit_that_kills()
    {
        SimPlayer warrior = Warrior();

        CombatRules.HitPlayer(warrior, 1000);

        Assert.True(warrior.IsDead);
        Assert.Equal(0u, warrior.CurrentPower);
    }

    [Fact]
    public void Empty_fury_on_the_hit_that_kills()
    {
        SimPlayer warrior = Warrior();
        warrior.CurrentPower = 60;

        CombatRules.HitPlayer(warrior, 1000);

        Assert.True(warrior.IsDead);
        Assert.Equal(0u, warrior.CurrentPower);
    }

    [Fact]
    public void Keep_mana_on_the_hit_that_kills()
    {
        SimPlayer wizard = SimPlayer.Create(Data, CharacterClass.Wizard, 1, []);
        uint mana = wizard.CurrentPower!.Value;

        CombatRules.HitPlayer(wizard, 100_000);

        Assert.True(wizard.IsDead);
        Assert.Equal(mana, wizard.CurrentPower);
    }

    [Fact]
    public void Cap_a_creatures_haste_for_cast_times_and_cooldowns()
    {
        CreatureTemplate alpha = Data.Creature(8);   // Bramblemaw Alpha: Howling Roar winds up for 1 s
        var hasty = new SimCreature
        {
            Name = "hasty",
            Template = alpha,
            Derived = Data.CreatureStats.Derive(alpha, 1),
            HasteCap = 50f,
            HastePct = 80f,
        };
        var roar = new SimAbility(Data.Abilities.TryGet(new AbilityId(310), out AbilityTemplate? row) ? row : throw new InvalidOperationException());

        Assert.Equal(50f, CombatRules.EffectiveHaste(hasty));
        Assert.Equal(roar.Metadata.CastTime / 1.5f, CombatRules.CastTime(hasty, roar), precision: 5);
        Assert.Equal(roar.Metadata.Cooldown / 1.5f, CombatRules.CooldownAfterFire(hasty, roar), precision: 5);
        Assert.Equal(2.25f / 1.5f, hasty.SwingInterval, precision: 5);
    }

    [Fact]
    public void Wait_a_creatures_swing_interval_after_its_basic()
    {
        SimCreature boar = Boar();

        Assert.Equal(2.25f, boar.SwingInterval);
        Assert.Equal(2.25f, CombatRules.CooldownAfterFire(boar, boar.Basic!));
        Assert.Equal(10f, CombatRules.CooldownAfterFire(boar, boar.Specials.Single()));   // Trample, 10 s
    }

    [Fact]
    public void Divide_a_characters_cooldown_by_its_haste()
    {
        SimPlayer warrior = Warrior(1, 7);   // Bramblesteel Sword: 3 % haste

        Assert.Equal(3f, warrior.HastePct);
        Assert.Equal(0.8f / 1.03f, CombatRules.CooldownAfterFire(warrior, warrior.Ability(200)), precision: 5);
    }

    [Fact]
    public void Choose_a_ready_special_before_the_basic_and_never_blight_spit()
    {
        SimCreature boar = Boar();
        Assert.Equal(301u, boar.Choose()!.Id);         // Trample ready
        boar.Specials.Single().CooldownLeft = 5f;
        Assert.Equal(300u, boar.Choose()!.Id);         // Gore

        SimCreature fly = SimCreature.Create(Data, Data.Creature(6), 1, 0);
        Assert.Equal(304u, fly.Choose()!.Id);          // Sting, never Blight Spit in melee
    }

    [Fact]
    public void Restore_no_more_than_the_health_missing()
    {
        SimPlayer healer = SimPlayer.Create(Data, CharacterClass.Healer, 1, []);
        healer.CurrentHealth = healer.Health - 5;

        Assert.Equal(5u, CombatRules.HealPlayer(healer, 40));
        Assert.Equal(healer.Health, healer.CurrentHealth);
    }
}
