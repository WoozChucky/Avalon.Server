using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;
using Avalon.World.Auras;
using Avalon.World.Entities;
using Avalon.World.Public.Units;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Auras;

/// <summary>
/// Ticks on absolute time, the last at expiry, caught up after a stall; credit to a caster only while it is here; and
/// every way an aura ends: expiry, death, an unloaded template, a creature walking home.
/// </summary>
public class AuraTickShould
{
    private static readonly AuraId Bleed = new(901);
    private static readonly AuraId Renew = new(904);
    private readonly HashSet<IUnit> _walkingHome = [];
    private readonly AuraHarness _h;

    public AuraTickShould()
    {
        _h = new AuraHarness(returningHome: unit => _walkingHome.Contains(unit));
        _h.Use(AuraTestData.Bleed(), AuraTestData.Crippled(), AuraTestData.Renew());
    }

    [Fact]
    public void Tick_on_schedule_and_expire_after_the_last_tick()
    {
        CharacterEntity warrior = _h.Player(910_101);
        Creature boar = _h.Creature(910_901);
        _h.Auras.Apply(warrior, boar, Bleed, AuraSource.None);

        _h.Advance(TimeSpan.FromMilliseconds(2999));
        _h.Auras.Update();
        Assert.Equal(1000u, boar.CurrentHealth);

        _h.Advance(TimeSpan.FromMilliseconds(1));
        _h.Auras.Update();
        Assert.Equal(997u, boar.CurrentHealth);   // 12 over 4 ticks: 3 a tick, the first at 3 s
        Assert.Equal(1, boar.Auras.Count);

        _h.Advance(TimeSpan.FromSeconds(9));
        _h.Auras.Update();

        Assert.Equal(1000u - 4 * 3u, boar.CurrentHealth);   // the last at 12 s
        Assert.Equal(0, boar.Auras.Count);
        Assert.Equal(AuraChangeKind.Removed, boar.Auras.Changes[^1].Kind);
    }

    /// <summary>A stall of a minute delivers the four owed ticks once, then the aura is gone.</summary>
    [Fact]
    public void Catch_up_every_owed_tick_after_a_stall_and_no_more()
    {
        Creature boar = _h.Creature(910_902);
        _h.Auras.Apply(_h.Player(910_102), boar, Bleed, AuraSource.None);

        _h.Advance(TimeSpan.FromMinutes(1));
        _h.Auras.Update();
        _h.Auras.Update();

        Assert.Equal(1000u - 12u, boar.CurrentHealth);
        Assert.Equal(0, boar.Auras.Count);
        _h.Outcomes.ReceivedWithAnyArgs(4).PeriodicTick(default, default!, default, default!, default, default);
    }

    /// <summary>A copy's fraction is carried from tick to tick, caught up or not, so its ticks add up to its total.</summary>
    [Fact]
    public void Carry_the_fraction_of_a_point_across_ticks_so_they_add_up_to_the_total()
    {
        _h.Use(new AuraTemplate
        {
            Id = new AuraId(907), Name = "Graze", Icon = "graze", Kind = AuraKind.Harmful, DurationMs = 12000,
            TickIntervalMs = 3000, PeriodicKind = AuraPeriodicKind.Damage, PeriodicBase = 10f,
            Stacking = AuraStacking.Refresh, MaxStacks = 1,
        });
        Creature boar = _h.Creature(910_911);
        _h.Auras.Apply(_h.Player(910_113), boar, new AuraId(907), AuraSource.None);

        _h.Advance(TimeSpan.FromSeconds(6));
        _h.Auras.Update();
        Assert.Equal(1000u - 5u, boar.CurrentHealth);   // 2.5 a tick: two owed at once deal 5 between them

        _h.Advance(TimeSpan.FromSeconds(3));
        _h.Auras.Update();
        _h.Advance(TimeSpan.FromSeconds(3));
        _h.Auras.Update();

        Assert.Equal(1000u - 10u, boar.CurrentHealth);
        Assert.Equal(0, boar.Auras.Count);
    }

    [Fact]
    public void Multiply_a_tick_by_the_stacks()
    {
        CharacterEntity warrior = _h.Player(910_103);
        Creature boar = _h.Creature(910_903);
        for (int cast = 0; cast < 3; cast++)
            _h.Auras.Apply(warrior, boar, Bleed, AuraSource.None);

        _h.Advance(TimeSpan.FromSeconds(3));
        _h.Auras.Update();

        Assert.Equal(1000u - 9u, boar.CurrentHealth);
    }

    [Fact]
    public void Heal_over_time()
    {
        CharacterEntity healer = _h.Player(910_104);
        CharacterEntity friend = _h.Player(910_105);
        friend.CurrentHealth = 400;
        _h.Auras.Apply(healer, friend, Renew, AuraSource.None);

        _h.Advance(TimeSpan.FromSeconds(3));
        _h.Auras.Update();

        Assert.Equal(406u, friend.CurrentHealth);   // 24 over 4 ticks
        _h.Outcomes.Received(1).PeriodicTick(healer, friend, 6u, Renew, HitResult.None, true);
    }

    [Fact]
    public void Credit_the_caster_only_while_it_is_alive_here()
    {
        CharacterEntity warrior = _h.Player(910_106);
        Creature boar = _h.Creature(910_904);
        _h.Auras.Apply(warrior, boar, Bleed, AuraSource.None);

        _h.Advance(TimeSpan.FromSeconds(3));
        _h.Auras.Update();
        _h.Characters.Remove(warrior.Guid);   // it left the instance
        _h.Advance(TimeSpan.FromSeconds(3));
        _h.Auras.Update();

        _h.Outcomes.Received(1).PeriodicTick(warrior, boar, 3u, Bleed, HitResult.None, false);
        _h.Outcomes.Received(1).PeriodicTick(null, boar, 3u, Bleed, HitResult.None, false);
    }

    /// <summary>The caster logged out; the last tick kills; the kill counts and nobody is credited.</summary>
    [Fact]
    public void Kill_with_a_tick_after_the_caster_left_and_credit_nobody()
    {
        CharacterEntity warrior = _h.Player(910_107);
        Creature boar = _h.Creature(910_905, health: 3);
        _h.Auras.Apply(warrior, boar, Bleed, AuraSource.None);
        _h.Characters.Remove(warrior.Guid);

        _h.Advance(TimeSpan.FromSeconds(3));
        _h.Auras.Update();

        Assert.Equal(0u, boar.CurrentHealth);
        _h.Outcomes.Received(1).CreatureKilled(boar, null);
        Assert.Equal(0, boar.Auras.Count);
    }

    /// <summary>A killing tick among several owed ends the run: the dead take no more ticks.</summary>
    [Fact]
    public void Tick_nothing_more_on_a_unit_a_tick_killed()
    {
        Creature boar = _h.Creature(910_912, health: 4);
        _h.Auras.Apply(_h.Player(910_114), boar, Bleed, AuraSource.None);
        _h.Auras.Apply(_h.Player(910_115), boar, Renew, AuraSource.None);

        _h.Advance(TimeSpan.FromMinutes(1));
        _h.Auras.Update();
        _h.Auras.Update();

        Assert.Equal(0u, boar.CurrentHealth);
        Assert.Equal(0, boar.Auras.Count);
        _h.Outcomes.ReceivedWithAnyArgs(2).PeriodicTick(default, default!, default, default!, default, default);
        _h.Outcomes.ReceivedWithAnyArgs(1).CreatureKilled(default!, default);
    }

    [Fact]
    public void Credit_nobody_for_a_caster_that_died()
    {
        CharacterEntity warrior = _h.Player(910_108);
        Creature boar = _h.Creature(910_906);
        _h.Auras.Apply(warrior, boar, Bleed, AuraSource.None);
        warrior.IsDead = true;

        _h.Advance(TimeSpan.FromSeconds(3));
        _h.Auras.Update();

        _h.Outcomes.Received(1).PeriodicTick(null, boar, 3u, Bleed, HitResult.None, false);
    }

    [Fact]
    public void End_every_aura_when_its_unit_dies_and_give_back_its_speed()
    {
        CharacterEntity hunter = _h.Player(910_109);
        Creature boar = _h.Creature(910_907);
        _h.Auras.Apply(hunter, boar, Bleed, AuraSource.None);
        _h.Auras.Apply(hunter, boar, new AuraId(903), AuraSource.None);
        boar.CurrentHealth = 0;

        _h.Auras.Update();

        Assert.Equal(0, boar.Auras.Count);
        Assert.Equal(1f, boar.SpeedFactor);
    }

    [Fact]
    public void Expire_an_aura_whose_template_is_no_longer_loaded_without_ticking_it()
    {
        Creature boar = _h.Creature(910_908);
        _h.Auras.Apply(_h.Player(910_110), boar, Bleed, AuraSource.None);
        _h.Use(AuraTestData.Renew());

        _h.Advance(TimeSpan.FromSeconds(3));
        _h.Auras.Update();

        Assert.Equal(0, boar.Auras.Count);
        Assert.Equal(1000u, boar.CurrentHealth);
    }

    /// <summary>A creature walking home loses the harmful auras a fight put on it.</summary>
    [Fact]
    public void End_the_harmful_auras_of_a_creature_walking_home()
    {
        Creature boar = _h.Creature(910_909);
        _h.Auras.Apply(_h.Player(910_111), boar, Bleed, AuraSource.None);
        _walkingHome.Add(boar);

        _h.Auras.Update();

        Assert.Equal(0, boar.Auras.Count);
    }

    /// <summary>Only the harmful auras end with the fight: a helpful one on a creature walking home stays and ticks.</summary>
    [Fact]
    public void Keep_the_helpful_auras_of_a_creature_walking_home()
    {
        Creature boar = _h.Creature(910_913);
        boar.CurrentHealth = 900;
        CharacterEntity caster = _h.Player(910_116);
        _h.Auras.Apply(caster, boar, Bleed, AuraSource.None);
        _h.Auras.Apply(caster, boar, Renew, AuraSource.None);
        _walkingHome.Add(boar);

        _h.Advance(TimeSpan.FromSeconds(3));
        _h.Auras.Update();

        ActiveAura held = Assert.Single(boar.Auras.All);
        Assert.Equal(Renew.Value, held.Id.Value);
        Assert.Equal(906u, boar.CurrentHealth);
    }

    /// <summary>The aura system's one answer to "walking home" refuses a harmful aura as its tick ends one.</summary>
    [Fact]
    public void Refuse_a_harmful_aura_on_a_creature_walking_home()
    {
        Creature boar = _h.Creature(910_914);
        _walkingHome.Add(boar);

        AuraApplyResult harmful = _h.Auras.Apply(_h.Player(910_117), boar, Bleed, AuraSource.None);
        AuraApplyResult helpful = _h.Auras.Apply(_h.Player(910_118), boar, Renew, AuraSource.None);

        Assert.Equal(AuraApplyResult.Refused, harmful);
        Assert.Equal(AuraApplyResult.Applied, helpful);
    }

    /// <summary>
    /// A tick whose report throws has still taken its points: the carry it left is kept, the rest of the pass still ticks,
    /// and a throw on every pass stops none of the later ones.
    /// </summary>
    [Fact]
    public void Keep_the_carry_of_a_tick_that_threw_and_tick_the_rest_of_the_pass()
    {
        _h.Use(AuraTestData.Bleed(), new AuraTemplate
        {
            Id = new AuraId(907), Name = "Graze", Icon = "graze", Kind = AuraKind.Harmful, DurationMs = 12000,
            TickIntervalMs = 3000, PeriodicKind = AuraPeriodicKind.Damage, PeriodicBase = 10f,
            Stacking = AuraStacking.Refresh, MaxStacks = 1,
        });
        Creature thrower = _h.Creature(910_915);
        Creature other = _h.Creature(910_916);
        _h.Auras.Apply(_h.Player(910_119), thrower, new AuraId(907), AuraSource.None);
        _h.Auras.Apply(_h.Player(910_120), other, Bleed, AuraSource.None);
        _h.Outcomes
            .When(o => o.PeriodicTick(Arg.Any<IUnit?>(), thrower, Arg.Any<uint>(), Arg.Any<AuraId>(), Arg.Any<HitResult>(),
                Arg.Any<bool>()))
            .Do(_ => throw new InvalidOperationException("report failed"));

        _h.Advance(TimeSpan.FromSeconds(3));
        _h.Auras.Update();

        Assert.Equal(998u, thrower.CurrentHealth);   // 2.5: 2 dealt before the report threw
        Assert.Equal(0.5d, Assert.Single(thrower.Auras.All).PeriodicCarry, 6);
        Assert.Equal(997u, other.CurrentHealth);

        _h.Advance(TimeSpan.FromSeconds(3));
        _h.Auras.Update();

        Assert.Equal(995u, thrower.CurrentHealth);   // 2.5 + 0.5 carried: 3
        Assert.Equal(994u, other.CurrentHealth);
    }

    /// <summary>
    /// A stats refresh that throws while a dead character's auras end stops neither the instance's pass nor the next one:
    /// the units after it still tick.
    /// </summary>
    [Fact]
    public void Tick_the_other_units_when_ending_a_units_auras_throws()
    {
        _h.Use(AuraTestData.Bleed(), AuraTestData.Fortified());
        CharacterEntity fallen = _h.Player(910_123);
        Creature boar = _h.Creature(910_917);
        _h.Auras.Apply(fallen, fallen, new AuraId(905), AuraSource.None);
        _h.Auras.Apply(_h.Player(910_124), boar, Bleed, AuraSource.None);
        fallen.IsDead = true;
        _h.DataFails = true;

        _h.Advance(TimeSpan.FromSeconds(3));
        _h.Auras.Update();
        Assert.Equal(997u, boar.CurrentHealth);

        _h.Advance(TimeSpan.FromSeconds(3));
        _h.Auras.Update();
        Assert.Equal(994u, boar.CurrentHealth);
    }

    /// <summary>A heal over time on a unit at full health restores nothing and reports nothing.</summary>
    [Fact]
    public void Restore_and_report_nothing_on_a_unit_at_full_health()
    {
        CharacterEntity healer = _h.Player(910_121);
        CharacterEntity friend = _h.Player(910_122);
        _h.Auras.Apply(healer, friend, Renew, AuraSource.None);

        _h.Advance(TimeSpan.FromSeconds(3));
        _h.Auras.Update();

        Assert.Equal(500u, friend.CurrentHealth);
        _h.Outcomes.DidNotReceiveWithAnyArgs().PeriodicTick(default, default!, default, default!, default, default);
    }

    /// <summary>The tick path: no unit holding an aura costs nothing (#640).</summary>
    [Fact]
    public void Allocate_nothing_while_no_unit_holds_an_aura()
    {
        _h.Player(910_112);
        _h.Creature(910_910);
        _h.Auras.Update();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
            _h.Auras.Update();

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
