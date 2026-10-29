using Avalon.Balance.Core;
using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.State;
using Xunit;

namespace Avalon.Balance.UnitTests;

public class CombatRulesShould
{
    private static BalanceData Data => TestData.Seeded;

    private static SimPlayer Warrior(ushort level = 1, params ulong[] gear) =>
        SimPlayer.Create(Data, CharacterClass.Warrior, level, gear.Select(Data.Worn));

    private static SimCreature Boar(ushort level = 1) => SimCreature.Create(Data, Data.Creature(4), level, 0);

    [Fact]
    public void Start_a_warrior_full_of_health_and_mana()
    {
        SimPlayer warrior = Warrior();

        Assert.Equal(240u, warrior.CurrentHealth);
        Assert.Equal(PowerType.Mana, warrior.PowerType);
        Assert.Equal(180u, warrior.CurrentPower);
        Assert.Equal(180u, warrior.Power);
        Assert.Equal([319u], warrior.Abilities.Select(a => a.Id));   // its starter alone (#1181)
    }

    [Fact]
    public void Start_a_wizard_full_of_mana_regenerating_a_share_of_its_maximum()
    {
        var wizard = SimPlayer.Create(Data, CharacterClass.Wizard, 1, []);

        Assert.Equal(wizard.Power, wizard.CurrentPower);
        Assert.Equal(ManaRegenRates.Of(new RegenConfiguration(), wizard.Power!.Value, 0f), wizard.ManaRegen);
    }

    [Fact(Skip = BalancePause.Reason)]
    public void Deal_cleave_through_the_real_resolver()
    {
        SimPlayer warrior = Warrior();
        SimCreature boar = Boar(1);   // level 1: armour 0

        (uint damage, _) = CombatRules.Damage(warrior, boar, warrior.Ability(200), Data.Combat.Formula, CombatRandom.Steady);

        Assert.Equal(25u, damage);    // floor(25.8)
    }

    /// <summary>A hit on a creature takes the health it deals, and a dodged one (0) takes nothing.</summary>
    [Theory]
    [InlineData(10u)]
    [InlineData(0u)]
    public void Take_the_health_a_hit_on_a_creature_deals(uint damage)
    {
        SimCreature boar = Boar();
        uint health = boar.CurrentHealth;

        Assert.Equal(damage, CombatRules.HitCreature(boar, damage));
        Assert.Equal(health - damage, boar.CurrentHealth);
    }

    /// <summary>#1181: a hit on the player takes health and leaves its mana alone.</summary>
    [Fact]
    public void Take_the_health_a_hit_takes_and_leave_the_mana()
    {
        SimPlayer warrior = Warrior();
        warrior.CurrentPower = 60;

        uint lost = CombatRules.HitPlayer(warrior, 48);

        Assert.Equal(48u, lost);
        Assert.Equal(60u, warrior.CurrentPower);
    }

    /// <summary>#1181: the hit that kills leaves the mana as it was, for every class.</summary>
    [Theory]
    [InlineData(CharacterClass.Warrior)]
    [InlineData(CharacterClass.Wizard)]
    public void Keep_mana_on_the_hit_that_kills(CharacterClass @class)
    {
        var player = SimPlayer.Create(Data, @class, 1, []);
        player.CurrentPower = 60;

        CombatRules.HitPlayer(player, 100_000);

        Assert.True(player.IsDead);
        Assert.Equal(60u, player.CurrentPower);
    }

    [Fact(Skip = BalancePause.Reason)]
    public void Cap_a_creatures_haste_for_cast_times_and_cooldowns()
    {
        CreatureTemplate alpha = Data.Creature(8);   // Bramblemaw Alpha: Howling Roar winds up for 1 s
        var hasty = new SimCreature
        {
            Name = "hasty",
            Template = alpha,
            Derived = Data.CreatureStats.Derive(alpha, 1),
            // #1178: the one haste became three speeds; this is the compile fix alone (#1183: skipped, never edited to pass).
            AttackSpeedCap = 50f,
            CastSpeedCap = 50f,
            CooldownRecoveryCap = 50f,
            AttackSpeedPct = 80f,
            CastSpeedPct = 80f,
            CooldownRecoveryPct = 80f,
        };
        var roar = new SimAbility(Data.Abilities.TryGetValue(new AbilityId(310), out AbilityTemplate? row) ? row : throw new InvalidOperationException());

        Assert.Equal(50f, CombatRules.PaceOf(hasty).CastSpeedPct);
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

    /// <summary>
    /// #1178: an attack waits its weapon's time over the attack speed. Bare, the Warrior's fist; in the forest profile,
    /// the fixed copy of its sword (item 7) at the midpoint of its range. Agility 20 x 0.1 % attack speed either way.
    /// </summary>
    [Theory]
    [InlineData("none")]
    [InlineData("forest")]
    public void Wait_an_attacks_weapon_time_over_attack_speed(string gear)
    {
        var warrior = SimPlayer.Create(Data, CharacterClass.Warrior, 1, TestData.Gear(gear, CharacterClass.Warrior));
        int attackMs = gear == "none" ? 816 : TestData.MidpointOf(7, ItemBaseStat.AttackTime);

        Assert.Equal(2f, warrior.Pace.AttackSpeedPct, precision: 4);
        Assert.Equal(attackMs / 1000f / 1.02f, CombatRules.CooldownAfterFire(warrior, warrior.Ability(319)), precision: 5);
    }

    [Fact]
    public void Choose_a_ready_special_before_the_basic_and_never_blight_spit_in_melee()
    {
        SimCreature boar = Boar();
        Assert.Equal(301u, boar.Choose()!.Id);         // Trample ready
        boar.Specials.Single().CooldownLeft = 5f;
        Assert.Equal(300u, boar.Choose()!.Id);         // Gore

        var fly = SimCreature.Create(Data, Data.Creature(6), 1, 0);
        Assert.Equal(317u, fly.Choose()!.Id);          // Venom Spit ready
        fly.Specials.Single(s => s.Id == 317).CooldownLeft = 5f;
        Assert.Equal(304u, fly.Choose()!.Id);          // Sting, never Blight Spit in melee
    }

    [Fact]
    public void Restore_no_more_than_the_health_missing()
    {
        var cleric = SimPlayer.Create(Data, CharacterClass.Cleric, 1, []);
        cleric.CurrentHealth = cleric.Health - 5;

        Assert.Equal(5u, CombatRules.HealPlayer(cleric, 40));
        Assert.Equal(cleric.Health, cleric.CurrentHealth);
    }
}
