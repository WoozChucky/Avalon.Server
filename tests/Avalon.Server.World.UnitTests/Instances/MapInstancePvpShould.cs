using Avalon.Common;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.World.Abilities.Targeting;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Pvp;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Instances;

/// <summary>The PvP flag through a real MapInstance (#164). Character ids are unique to this class (164_8xx).</summary>
public class MapInstancePvpShould
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1d / 60d);
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Seven 60 Hz ticks: past the 0.1 s state broadcast interval.</summary>
    private static void TickUntilBroadcast(MapInstance instance)
    {
        for (int i = 0; i < 7; i++)
        {
            instance.Update(Tick);
        }
    }

    private readonly FixedTimeProvider _clock = new(Now);
    private readonly PvpToggle _toggle;

    public MapInstancePvpShould() =>
        _toggle = new PvpToggle(Options.Create(new GameConfiguration { PvpOffDelay = TimeSpan.FromMinutes(5) }), _clock);

    /// <summary>The instance, its combat service and the test share one toggle and one clock.</summary>
    private MapInstance Build(MapType mapType = MapType.Normal, Avalon.World.Combat.ICombatRandom? random = null) =>
        TestMapInstances.Build(NewWorld(), pvp: _toggle, mapType: mapType, random: random);

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
        instance.Update(Tick);
        player.Sent.Clear();

        _clock.Now = _clock.Now.AddMinutes(5);
        instance.Update(Tick);

        Assert.False(player.Character.PvpEnabled);
        Assert.Equal((false, 0u), PvpStates(player).Select(s => (s.Enabled, s.OffInMs)).Single());
    }

    [Fact]
    public void Send_nothing_on_a_tick_before_the_timer_is_due()
    {
        using MapInstance instance = Build();
        MapInstanceClient player = Join(instance, 164_811);
        _toggle.Request(player.Character);
        _toggle.Request(player.Character);
        instance.Update(Tick);
        player.Sent.Clear();

        _clock.Now = _clock.Now.AddMinutes(4);
        instance.Update(Tick);

        Assert.True(player.Character.PvpEnabled);
        Assert.Empty(PvpStates(player));
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

        var wolf = Substitute.For<ICreature>();
        wolf.Guid.Returns(new ObjectGuid(ObjectType.Creature, 164_829));
        instance.CombatService.ApplyDamage(wolf, a.Character, 5);
        Assert.Equal(before, a.Character.PvpOffAt);

        instance.CombatService.ApplyDamage(a.Character, b.Character, 5);
        Assert.Equal(_clock.Now.UtcDateTime.AddMinutes(5), a.Character.PvpOffAt);
        Assert.Equal(_clock.Now.UtcDateTime.AddMinutes(5), b.Character.PvpOffAt);
    }

    [Fact]
    public void Tell_a_character_entering_the_instance_its_flag_and_timer()
    {
        using MapInstance instance = Build();
        CharacterEntity relogged = TestCharacters.New(164_831);
        relogged.Data!.PvpEnabled = true;
        relogged.Data.PvpOffAt = Now.UtcDateTime.AddMinutes(2);

        MapInstanceClient player = Join(instance, relogged);
        instance.Update(Tick);

        SPvpStatePacket state = Assert.Single(PvpStates(player));
        Assert.True(state.Enabled);
        Assert.Equal(120_000u, state.OffInMs);
    }

    [Fact]
    public void Turn_off_a_flag_whose_timer_ran_out_while_offline_and_say_so_once_on_entry()
    {
        using MapInstance instance = Build();
        CharacterEntity relogged = TestCharacters.New(164_861);
        relogged.Data!.PvpEnabled = true;
        relogged.Data.PvpOffAt = Now.UtcDateTime.AddMinutes(-1);

        MapInstanceClient player = Join(instance, relogged);
        instance.Update(Tick);

        Assert.False(relogged.PvpEnabled);
        Assert.Null(relogged.PvpOffAt);
        Assert.Equal((false, 0u), PvpStates(player).Select(s => (s.Enabled, s.OffInMs)).Single());
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
        b.Character.ApplyStats(new Avalon.World.Characters.DerivedCharacterStats(MaxHealth: 100, MaxPower: 0, Stamina: 0,
            Strength: 0, Agility: 0, Intellect: 0, Armor: 0, BlockPct: 0f, DodgePct: 30f, CritPct: 0f, AttackDamage: 0,
            AbilityDamage: 0), Avalon.World.Characters.CurrentValues.Refill);
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

    /// <summary>A town accepts the toggle and answers it, but never lets two flagged players fight.</summary>
    [Fact]
    public void Accept_a_toggle_in_a_town_while_hostility_still_refuses_flagged_players_there()
    {
        using MapInstance instance = Build(MapType.Town);
        MapInstanceClient a = Join(instance, 164_891);
        MapInstanceClient b = Join(instance, 164_892);
        instance.Update(Tick);
        a.Sent.Clear();

        _toggle.Toggle(a.Connection);
        _toggle.Toggle(b.Connection);

        Assert.Equal((true, 0u), PvpStates(a).Select(s => (s.Enabled, s.OffInMs)).Single());
        Assert.True(a.Character.PvpEnabled);
        Assert.True(b.Character.PvpEnabled);
        Assert.False(Hostility.IsHostile(a.Character, b.Character, instance.MapType));
        Assert.False(Hostility.IsHostile(b.Character, a.Character, instance.MapType));
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

        instance.Update(Tick);
        foreach (MapInstanceClient c in clients)
        {
            c.Sent.Clear();
        }
    }

    /// <summary>The client's countdown is exact: a hit that moves the deadline by more than a second is re-sent.</summary>
    [Fact]
    public void Resend_each_players_countdown_once_when_a_hit_moves_it()
    {
        using MapInstance instance = Build();
        MapInstanceClient a = Join(instance, 164_901);
        MapInstanceClient b = Join(instance, 164_902);
        FlagAndAskOff(instance, a, b);

        _clock.Now = _clock.Now.AddMinutes(1);
        instance.CombatService.ApplyDamage(a.Character, b.Character, 5);
        instance.Update(Tick);
        instance.Update(Tick);

        foreach (MapInstanceClient c in new[] { a, b })
        {
            Assert.Equal((true, 300_000u), PvpStates(c).Select(s => (s.Enabled, s.OffInMs)).Single());
        }
    }

    [Fact]
    public void Send_no_second_countdown_for_a_hit_in_the_same_second()
    {
        using MapInstance instance = Build();
        MapInstanceClient a = Join(instance, 164_911);
        MapInstanceClient b = Join(instance, 164_912);
        FlagAndAskOff(instance, a, b);
        _clock.Now = _clock.Now.AddMinutes(1);
        instance.CombatService.ApplyDamage(a.Character, b.Character, 5);
        instance.Update(Tick);
        a.Sent.Clear();
        b.Sent.Clear();

        _clock.Now = _clock.Now.AddMilliseconds(500);
        instance.CombatService.ApplyDamage(a.Character, b.Character, 5);
        instance.Update(Tick);

        Assert.Empty(PvpStates(a));
        Assert.Empty(PvpStates(b));
    }

    [Fact]
    public void Send_one_more_countdown_for_a_hit_two_seconds_later()
    {
        using MapInstance instance = Build();
        MapInstanceClient a = Join(instance, 164_921);
        MapInstanceClient b = Join(instance, 164_922);
        FlagAndAskOff(instance, a, b);
        _clock.Now = _clock.Now.AddMinutes(1);
        instance.CombatService.ApplyDamage(a.Character, b.Character, 5);
        instance.Update(Tick);
        a.Sent.Clear();
        b.Sent.Clear();

        _clock.Now = _clock.Now.AddSeconds(2);
        instance.CombatService.ApplyDamage(a.Character, b.Character, 5);
        instance.Update(Tick);

        foreach (MapInstanceClient c in new[] { a, b })
        {
            Assert.Equal((true, 300_000u), PvpStates(c).Select(s => (s.Enabled, s.OffInMs)).Single());
        }
    }

    [Fact]
    public void Send_no_countdown_for_creature_combat()
    {
        using MapInstance instance = Build();
        MapInstanceClient a = Join(instance, 164_931);
        FlagAndAskOff(instance, a);
        var wolf = Substitute.For<ICreature>();
        wolf.Guid.Returns(new ObjectGuid(ObjectType.Creature, 164_939));

        _clock.Now = _clock.Now.AddMinutes(1);
        instance.CombatService.ApplyDamage(wolf, a.Character, 5);
        instance.CombatService.ApplyDamage(a.Character, wolf, 5);
        instance.Update(Tick);

        Assert.Empty(PvpStates(a));
    }

    [Fact]
    public void Send_no_countdown_while_no_timer_runs()
    {
        using MapInstance instance = Build();
        MapInstanceClient a = Join(instance, 164_941);
        MapInstanceClient b = Join(instance, 164_942);
        instance.Update(Tick);
        foreach (MapInstanceClient c in new[] { a, b })
        {
            _toggle.Request(c.Character);   // on, no timer, never told
            c.Character.Health = 100;
            c.Character.CurrentHealth = 100;
            c.Sent.Clear();
        }

        instance.CombatService.ApplyDamage(a.Character, b.Character, 5);
        instance.Update(Tick);

        Assert.Empty(PvpStates(a));
        Assert.Empty(PvpStates(b));
    }

    [Fact]
    public void Tell_an_entering_character_once_only()
    {
        using MapInstance instance = Build();
        MapInstanceClient player = Join(instance, 164_841);

        instance.Update(Tick);
        instance.Update(Tick);

        SPvpStatePacket state = Assert.Single(PvpStates(player));
        Assert.False(state.Enabled);
        Assert.Equal(0u, state.OffInMs);
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
