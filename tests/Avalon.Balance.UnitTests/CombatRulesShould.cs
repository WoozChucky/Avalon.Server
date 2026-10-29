using Avalon.Balance.Core;
using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;
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

    /// <summary>Steady rolls the minimum and never evades, crits or blocks; a level-1 boar has no armour.</summary>
    [Fact]
    public void Deal_cleave_through_the_real_resolver()
    {
        SimPlayer warrior = Warrior().Holding(Data, 200);
        SimCreature boar = Boar(1);   // level 1: armour 0

        uint damage = CombatRules.Damage(warrior, boar, warrior.Ability(200), Data.Combat.Formula, CombatRandom.Steady).Total;

        Assert.Equal(17u, damage);    // floor(17.05725)
    }

    /// <summary>
    /// B-R6: a direct hit on a creature meets its ward first, for what the hit lets the ward absorb (all but its blight
    /// share), and health loses the rest; a dodged hit (0) takes nothing. What the hit cost is the ward's part plus the health.
    /// Any hit pauses the recharge, even one the ward does not meet: a blight point 3 s later leaves the ward as it was 2 s
    /// after that, short of the 4 s delay, when the next point it can absorb meets it.
    /// </summary>
    [Theory]
    [InlineData(10u, 0u, 10u, 0u, 10u)]
    [InlineData(0u, 0u, 0u, 0u, 0u)]
    [InlineData(30u, 20u, 30u, 0u, 10u)]
    [InlineData(30u, 50u, 30u, 20u, 0u)]
    [InlineData(30u, 50u, 10u, 40u, 20u)]   // a blight share
    [InlineData(30u, 50u, 0u, 50u, 30u)]    // all blight
    public void Take_the_health_a_hit_on_a_creature_deals(uint total, uint ward, uint absorbable, uint wardAfter, uint healthLost)
    {
        SimCreature boar = Boar();
        boar.ResetWard(ward);
        uint health = boar.CurrentHealth;

        DateTime start = FightSimulator.Epoch.UtcDateTime;
        WardedLoss loss = CombatRules.Land(boar,
            new HitDamage(total, HitResult.None, DamageType.Physical, DamageTypeMask.Physical, absorbable), start);

        Assert.Equal(wardAfter, boar.Ward);
        Assert.Equal(healthLost, health - boar.CurrentHealth);
        Assert.Equal(total, loss.Total);   // the boar outlives every row

        CombatRules.Land(boar, new HitDamage(1, HitResult.None, DamageType.Blight, DamageTypeMask.Blight, 0), start.AddSeconds(3));
        CombatRules.Land(boar, new HitDamage(1, HitResult.None, DamageType.Physical, DamageTypeMask.Physical, 1), start.AddSeconds(5));
        Assert.Equal(wardAfter == 0 ? 0u : wardAfter - 1, boar.Ward);
    }

    /// <summary>#1181: a hit on the player takes health and leaves its mana alone.</summary>
    [Fact]
    public void Take_the_health_a_hit_takes_and_leave_the_mana()
    {
        SimPlayer warrior = Warrior();
        warrior.ResetWard(0);   // the class's own ward would take the first points
        warrior.CurrentPower = 60;

        WardedLoss loss = CombatRules.Land(warrior,
            new HitDamage(48, HitResult.None, DamageType.Physical, DamageTypeMask.Physical, 48), FightSimulator.Epoch.UtcDateTime);

        Assert.Equal(new WardedLoss(0, 48), loss);
        Assert.Equal(60u, warrior.CurrentPower);
    }

    /// <summary>#1181: the hit that kills leaves the mana as it was, for every class, and empties the ward (B-R6).</summary>
    [Theory]
    [InlineData(CharacterClass.Warrior)]
    [InlineData(CharacterClass.Wizard)]
    public void Keep_mana_on_the_hit_that_kills(CharacterClass @class)
    {
        var player = SimPlayer.Create(Data, @class, 1, TestData.Gear("forest", @class));
        Assert.True(@class != CharacterClass.Wizard || player.Ward > 0, "a Wizard in cloth carries a ward");
        player.CurrentPower = 60;

        // All blight, so the ward absorbs none of it: only death empties it.
        CombatRules.Land(player, new HitDamage(100_000, HitResult.None, DamageType.Blight, DamageTypeMask.Blight, 0),
            FightSimulator.Epoch.UtcDateTime);

        Assert.True(player.IsDead);
        Assert.Equal(60u, player.CurrentPower);
        Assert.Equal(0u, player.Ward);   // death empties the ward (B-R6)
    }

    [Fact]
    public void Cap_a_creatures_speeds_for_cast_times_cooldowns_and_swings()
    {
        CreatureTemplate alpha = Data.Creature(8);   // Bramblemaw Alpha: Howling Roar winds up for 1 s
        var hasty = new SimCreature
        {
            Name = "hasty",
            Template = alpha,
            Derived = Data.CreatureStats.Derive(alpha, 1),
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
