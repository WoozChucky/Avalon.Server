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
}
