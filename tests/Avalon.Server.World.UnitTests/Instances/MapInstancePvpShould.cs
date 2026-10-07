using Avalon.Common;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Public.Creatures;
using Avalon.World.Pvp;
using Microsoft.Extensions.Options;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Instances;

/// <summary>The PvP flag through a real MapInstance (#164). Character ids are unique to this class (164_8xx).</summary>
public class MapInstancePvpShould
{
    private static readonly TimeSpan s_tick = TimeSpan.FromSeconds(1d / 60d);
    private static readonly DateTimeOffset s_now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Seven 60 Hz ticks: past the 0.1 s state broadcast interval.</summary>
    private static void TickUntilBroadcast(MapInstance instance)
    {
        for (int i = 0; i < 7; i++)
        {
            instance.Update(s_tick);
        }
    }

    private readonly FixedTimeProvider _clock = new(s_now);
    private readonly PvpToggle _toggle;

    public MapInstancePvpShould() =>
        _toggle = new PvpToggle(Options.Create(new GameConfiguration { PvpOffDelay = TimeSpan.FromMinutes(5) }), _clock);

    /// <summary>The instance, its combat service and the test share one toggle and one clock.</summary>
    private MapInstance Build(Avalon.Combat.ICombatRandom? random = null) =>
        TestMapInstances.Build(NewWorld(), pvp: _toggle, random: random);

    /// <summary>Both characters flagged with a running off timer, at full health.</summary>
    private void FlagWithTimer(params MapInstanceClient[] clients)
    {
        foreach (MapInstanceClient c in clients)
        {
            _toggle.Request(c.Character);
            _toggle.Request(c.Character);
            c.Character.Health = 100;
            c.Character.CurrentHealth = 100;
        }
    }

    private static List<SPvpStatePacket> PvpStates(MapInstanceClient client) =>
        client.Read<SPvpStatePacket>(NetworkPacketType.SMSG_PVP_STATE);

    [Fact]
    public void Turn_the_flag_off_on_the_tick_it_is_due_and_tell_the_player()
    {
        using MapInstance instance = Build();
        MapInstanceClient player = Join(instance, 164_801);
        _toggle.Request(player.Character);
        _toggle.Request(player.Character);
        instance.Update(s_tick);
        player.Sent.Clear();

        _clock.Now = _clock.Now.AddMinutes(5);
        instance.Update(s_tick);

        Assert.False(player.Character.PvpEnabled);
        Assert.Equal((false, 0u), PvpStates(player).Select(s => (s.Enabled, s.OffInMs)).Single());
    }

    [Fact]
    public void Restart_both_timers_on_a_player_hit_but_not_on_creature_combat()
    {
        using MapInstance instance = Build();
        MapInstanceClient a = Join(instance, 164_821);
        MapInstanceClient b = Join(instance, 164_822);
        foreach (MapInstanceClient c in new[] { a, b })
        {
            _toggle.Request(c.Character);
            _toggle.Request(c.Character);
            c.Character.Health = 100;
            c.Character.CurrentHealth = 100;
        }

        DateTime? before = a.Character.PvpOffAt;
        _clock.Now = _clock.Now.AddMinutes(1);

        ICreature wolf = Substitute.For<ICreature>();
        wolf.Guid.Returns(new ObjectGuid(ObjectType.Creature, 164_829));
        instance.CombatService.ApplyDamage(wolf, a.Character, 5);
        Assert.Equal(before, a.Character.PvpOffAt);

        instance.CombatService.ApplyDamage(a.Character, b.Character, 5);
        Assert.Equal(_clock.Now.UtcDateTime.AddMinutes(5), a.Character.PvpOffAt);
        Assert.Equal(_clock.Now.UtcDateTime.AddMinutes(5), b.Character.PvpOffAt);
    }

    /// <summary>
    /// A character entering the instance is told its own flag and any timer left, once; a timer that ran out while it
    /// was offline is expired first, so it hears (false, 0), not a stale "on".
    /// </summary>
    [Theory]
    [InlineData(false, null, false, 0u)]
    [InlineData(true, 2, true, 120_000u)]
    [InlineData(true, -1, false, 0u)]   // ran out while offline
    public void Tell_a_character_entering_the_instance_its_flag_and_timer_once(
        bool enabled, int? offInMinutes, bool told, uint offInMs)
    {
        using MapInstance instance = Build();
        CharacterEntity relogged = TestCharacters.New(164_831);
        relogged.Data!.PvpEnabled = enabled;
        relogged.Data.PvpOffAt = offInMinutes is { } minutes ? s_now.UtcDateTime.AddMinutes(minutes) : null;

        MapInstanceClient player = Join(instance, relogged);
        instance.Update(s_tick);
        instance.Update(s_tick);

        Assert.Equal((told, offInMs), PvpStates(player).Select(s => (s.Enabled, s.OffInMs)).Single());
        Assert.Equal(told, relogged.PvpEnabled);
    }

    /// <summary>
    /// Since #506 every hit that lands deals at least 1, so the hit that deals no damage is a dodge: it
    /// restarts neither timer.
    /// </summary>
    [Fact]
    public void Leave_the_timers_alone_on_a_hit_of_no_damage()
    {
        using MapInstance instance = Build(random: new Combat.ScriptedCombatRandom(0.0));
        MapInstanceClient a = Join(instance, 164_871);
        MapInstanceClient b = Join(instance, 164_872);
        b.Character.ApplyStats(new Avalon.Combat.DerivedCharacterStats(MaxHealth: 100, MaxPower: 0, Stamina: 0,
            Strength: 0, Agility: 0, Intellect: 0, Armor: 0, BlockPct: 0f, DodgePct: 30f, CritPct: 0f, AttackDamage: 0,
            AbilityDamage: 0), Avalon.World.Characters.CurrentValues.Refill, TestCombat.Formula);
        FlagWithTimer(a, b);
        DateTime? before = a.Character.PvpOffAt;
        _clock.Now = _clock.Now.AddMinutes(1);

        instance.CombatService.ApplyDamage(a.Character, b.Character, 10);

        Assert.Equal(100u, b.Character.CurrentHealth);
        Assert.Equal(before, a.Character.PvpOffAt);
        Assert.Equal(before, b.Character.PvpOffAt);
    }

    [Fact]
    public void Leave_the_timers_alone_on_a_hit_on_a_dead_player()
    {
        using MapInstance instance = Build();
        MapInstanceClient a = Join(instance, 164_881);
        MapInstanceClient b = Join(instance, 164_882);
        FlagWithTimer(a, b);
        b.Character.IsDead = true;
        DateTime? before = a.Character.PvpOffAt;
        _clock.Now = _clock.Now.AddMinutes(1);

        instance.CombatService.ApplyDamage(a.Character, b.Character, 5);

        Assert.Equal(before, a.Character.PvpOffAt);
        Assert.Equal(before, b.Character.PvpOffAt);
    }

    /// <summary>Flagged, then asked to turn off through the toggle, so each client has been told its countdown.</summary>
    private void FlagAndAskOff(MapInstance instance, params MapInstanceClient[] clients)
    {
        foreach (MapInstanceClient c in clients)
        {
            c.Character.Health = 100;
            c.Character.CurrentHealth = 100;
            _toggle.Request(c.Character);
            _toggle.Toggle(c.Connection);
        }

        instance.Update(s_tick);
        foreach (MapInstanceClient c in clients)
        {
            c.Sent.Clear();
        }
    }

    /// <summary>
    /// The client's countdown is exact: a hit that moves the deadline by more than a second is re-sent, once, to both
    /// players; a second hit within the second sends nothing, one two seconds later sends one more.
    /// </summary>
    [Theory]
    [InlineData(500, false)]
    [InlineData(2_000, true)]
    public void Resend_each_players_countdown_once_when_a_hit_moves_it(int nextHitMs, bool resentAgain)
    {
        using MapInstance instance = Build();
        MapInstanceClient a = Join(instance, 164_901);
        MapInstanceClient b = Join(instance, 164_902);
        FlagAndAskOff(instance, a, b);

        _clock.Now = _clock.Now.AddMinutes(1);
        instance.CombatService.ApplyDamage(a.Character, b.Character, 5);
        instance.Update(s_tick);
        instance.Update(s_tick);
        foreach (MapInstanceClient c in new[] { a, b })
        {
            Assert.Equal((true, 300_000u), PvpStates(c).Select(s => (s.Enabled, s.OffInMs)).Single());
            c.Sent.Clear();
        }

        _clock.Now = _clock.Now.AddMilliseconds(nextHitMs);
        instance.CombatService.ApplyDamage(a.Character, b.Character, 5);
        instance.Update(s_tick);

        List<(bool, uint)> expected = resentAgain ? [(true, 300_000u)] : [];
        foreach (MapInstanceClient c in new[] { a, b })
        {
            Assert.Equal(expected, PvpStates(c).Select(s => (s.Enabled, s.OffInMs)));
        }
    }

    [Fact]
    public void Replicate_the_flag_to_other_players()
    {
        using MapInstance instance = Build();
        MapInstanceClient flagged = Join(instance, 164_851);
        MapInstanceClient watcher = Join(instance, 164_852);
        TickUntilBroadcast(instance);
        watcher.Sent.Clear();

        _toggle.Request(flagged.Character);
        TickUntilBroadcast(instance);

        Assert.Contains(watcher.StateUpdates(), s => s.Guid == flagged.Character.Guid.RawValue && s.PvpEnabled == true);
    }
}
