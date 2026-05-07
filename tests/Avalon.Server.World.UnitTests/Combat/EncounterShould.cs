using System;
using Avalon.Common.Mathematics;
using Avalon.World.Combat;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Units;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Combat;

public class EncounterShould
{
    [Fact]
    public void Should_track_added_hostiles_and_players()
    {
        var enc = new Encounter(new CombatConfig());
        var hostile = Substitute.For<IUnit>();
        var player  = Substitute.For<IUnit>();

        enc.AddHostile(hostile);
        enc.AddPlayer(player);

        Assert.Contains(hostile, enc.Hostiles);
        Assert.Contains(player,  enc.Players);
    }

    [Fact]
    public void Should_seed_initial_threat_when_player_added()
    {
        var enc = new Encounter(new CombatConfig { InitialThreatSeed = 1.0f });
        var hostile = Substitute.For<IUnit>();
        var player  = Substitute.For<IUnit>();

        enc.AddHostile(hostile);
        enc.AddPlayer(player);

        var threats = enc.GetThreatList(hostile);
        Assert.Equal(1.0f, threats[player]);
    }

    [Fact]
    public void Should_seed_initial_threat_for_existing_players_when_hostile_added()
    {
        var enc = new Encounter(new CombatConfig { InitialThreatSeed = 1.0f });
        var hostile = Substitute.For<IUnit>();
        var player  = Substitute.For<IUnit>();

        // Player added first, then hostile.
        enc.AddPlayer(player);
        enc.AddHostile(hostile);

        var threats = enc.GetThreatList(hostile);
        Assert.Equal(1.0f, threats[player]);
    }

    [Fact]
    public void Should_add_threat_to_hostile_threat_list()
    {
        var enc = new Encounter(new CombatConfig { InitialThreatSeed = 0.0f });
        var hostile = Substitute.For<IUnit>();
        var player  = Substitute.For<IUnit>();

        enc.AddHostile(hostile);
        enc.AddPlayer(player);
        enc.AddThreat(hostile, player, 5.0f);

        var threats = enc.GetThreatList(hostile);
        Assert.Equal(5.0f, threats[player]);
    }

    [Fact]
    public void Should_return_attacker_with_highest_threat_as_top()
    {
        var enc = new Encounter(new CombatConfig { InitialThreatSeed = 0.0f });
        var hostile = Substitute.For<IUnit>();
        var p1 = Substitute.For<IUnit>();
        var p2 = Substitute.For<IUnit>();

        enc.AddHostile(hostile);
        enc.AddPlayer(p1);
        enc.AddPlayer(p2);
        enc.AddThreat(hostile, p1, 5.0f);
        enc.AddThreat(hostile, p2, 10.0f);

        Assert.Same(p2, enc.GetTopThreat(hostile));
    }

    [Fact]
    public void Should_remove_hostile_on_participant_died()
    {
        var enc = new Encounter(new CombatConfig());
        var hostile = Substitute.For<IUnit>();

        enc.AddHostile(hostile);
        enc.OnParticipantDied(hostile);

        Assert.DoesNotContain(hostile, enc.Hostiles);
        Assert.Empty(enc.GetThreatList(hostile));
    }

    [Fact]
    public void Should_remove_player_and_prune_threat_lists()
    {
        var enc = new Encounter(new CombatConfig { InitialThreatSeed = 0.0f });
        var h1 = Substitute.For<IUnit>();
        var h2 = Substitute.For<IUnit>();
        var p  = Substitute.For<IUnit>();

        enc.AddHostile(h1);
        enc.AddHostile(h2);
        enc.AddPlayer(p);
        enc.AddThreat(h1, p, 5.0f);
        enc.AddThreat(h2, p, 7.0f);

        enc.RemovePlayer(p);

        Assert.DoesNotContain(p, enc.Players);
        Assert.False(enc.GetThreatList(h1).ContainsKey(p));
        Assert.False(enc.GetThreatList(h2).ContainsKey(p));
    }

    [Fact]
    public void Should_decay_threat_per_tick()
    {
        var enc = new Encounter(new CombatConfig {
            DefaultDecayRatePerSecond = 1.0f,
            EngagementRadius = 1000.0f,
            InitialThreatSeed = 0
        });
        var hostile = Substitute.For<IUnit>();
        var attacker = Substitute.For<IUnit>();
        attacker.Position.Returns(new Vector3(0,0,0));
        hostile.Position.Returns(new Vector3(0,0,0));

        enc.AddHostile(hostile);
        enc.AddPlayer(attacker);
        enc.AddThreat(hostile, attacker, 5.0f);

        enc.Update(TimeSpan.FromSeconds(1));

        Assert.Equal(4.0f, enc.GetThreatList(hostile)[attacker], 3);
    }

    [Fact]
    public void Should_remove_threat_entry_when_decayed_to_zero()
    {
        var cfg = new CombatConfig {
            DefaultDecayRatePerSecond = 100.0f,
            EngagementRadius = 1000.0f,
            InitialThreatSeed = 0
        };
        var enc = new Encounter(cfg);
        var hostile = Substitute.For<IUnit>();
        var attacker = Substitute.For<IUnit>();
        hostile.Position.Returns(default(Vector3));
        attacker.Position.Returns(default(Vector3));

        enc.AddHostile(hostile);
        enc.AddPlayer(attacker);
        enc.AddThreat(hostile, attacker, 0.5f);

        enc.Update(TimeSpan.FromSeconds(1));

        Assert.False(enc.GetThreatList(hostile).ContainsKey(attacker));
    }

    [Fact]
    public void Should_accelerate_decay_when_attacker_outside_engagement_radius()
    {
        var cfg = new CombatConfig {
            DefaultDecayRatePerSecond = 1.0f,
            OutOfRangeDecayMultiplier = 5.0f,
            EngagementRadius = 1.0f,
            InitialThreatSeed = 0
        };
        var enc = new Encounter(cfg);
        var hostile = Substitute.For<IUnit>();
        var attacker = Substitute.For<IUnit>();
        hostile.Position.Returns(default(Vector3));
        attacker.Position.Returns(new Vector3(100,0,0));

        enc.AddHostile(hostile);
        enc.AddPlayer(attacker);
        enc.AddThreat(hostile, attacker, 10.0f);

        enc.Update(TimeSpan.FromSeconds(1));

        Assert.Equal(5.0f, enc.GetThreatList(hostile)[attacker], 3);
    }

    [Fact]
    public void Should_not_end_during_5_second_grace_window()
    {
        var enc = new Encounter(new CombatConfig {
            EncounterEndGraceSeconds = 5.0f,
            InitialThreatSeed = 0
        });
        var hostile = Substitute.For<IUnit>();
        enc.AddHostile(hostile);
        enc.OnParticipantDied(hostile);    // all hostiles dead

        enc.Update(TimeSpan.FromSeconds(2));   // less than grace

        Assert.False(enc.ShouldEnd);
    }

    [Fact]
    public void Should_end_when_all_hostiles_dead_after_grace()
    {
        var enc = new Encounter(new CombatConfig {
            EncounterEndGraceSeconds = 1.0f,
            InitialThreatSeed = 0
        });
        var hostile = Substitute.For<IUnit>();
        enc.AddHostile(hostile);
        enc.OnParticipantDied(hostile);
        // Force LastDamageTime back so grace has elapsed at Update time
        System.Threading.Thread.Sleep(1100);

        enc.Update(TimeSpan.FromSeconds(2));

        Assert.True(enc.ShouldEnd);
    }
}
