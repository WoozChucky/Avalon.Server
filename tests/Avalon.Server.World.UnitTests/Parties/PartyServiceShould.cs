using Avalon.Network.Packets.Party;
using Avalon.World.Parties;
using Xunit;

namespace Avalon.Server.World.UnitTests.Parties;

public class PartyServiceShould
{
    private readonly PartyTestWorld _w = new();

    [Fact]
    public void Form_a_party_when_an_invite_is_accepted()
    {
        PartyClient kaela = _w.Online(1, "Kaela", level: 7);
        PartyClient borin = _w.Online(2, "Borin");

        Assert.Equal(PartyResult.Ok, _w.Parties.Invite(kaela.Id, "borin"));
        SPartyInvitePacket invite = Assert.Single(borin.Invites());
        Assert.Equal("Kaela", invite.InviterName);
        Assert.Equal((ushort)7, invite.InviterLevel);
        Assert.Equal(60_000u, invite.ExpiresInMs);
        Assert.Null(_w.Parties.PartyOf(kaela.Id)); // not until accepted

        Assert.Equal(PartyResult.Ok, _w.Parties.Respond(borin.Id, accept: true));

        Party party = Assert.IsType<Party>(_w.Parties.PartyOf(kaela.Id));
        Assert.Same(party, _w.Parties.PartyOf(borin.Id));
        Assert.True(party.IsLeader(kaela.Id));
        Assert.Equal([kaela.Id, borin.Id], party.Members.Select(m => m.Id.Value));
        Assert.Equal(party.Id, kaela.Character.PartyId);
        Assert.Equal(party.Id, borin.Character.PartyId);

        SPartyRosterPacket roster = kaela.Rosters().Last();
        Assert.Equal(party.Id.Value, roster.PartyId);
        Assert.Equal(PartyExperienceMode.Even, roster.ExperienceMode);
        Assert.Equal(["Kaela", "Borin"], roster.Members.Select(m => m.Name));
        Assert.True(roster.Members[0].IsLeader);
        Assert.All(roster.Members, m => Assert.True(m.Online));
        Assert.Contains("Borin joined the party.", kaela.Lines());
        Assert.Contains("Borin joined the party.", borin.Lines());
    }

    [Fact]
    public void Refuse_invites_by_the_rules()
    {
        PartyClient a = _w.Online(1, "A");
        PartyClient b = _w.Online(2, "B");
        PartyClient c = _w.Online(3, "C");

        Assert.Equal(PartyResult.NotFound, _w.Parties.Invite(a.Id, "Nobody"));
        Assert.Equal(PartyResult.Self, _w.Parties.Invite(a.Id, "A"));
        Assert.Equal(PartyResult.Ok, _w.Parties.Invite(a.Id, "B"));
        Assert.Equal(PartyResult.InvitePending, _w.Parties.Invite(c.Id, "B"));
        _w.Parties.Respond(b.Id, accept: true);
        Assert.Equal(PartyResult.AlreadyInParty, _w.Parties.Invite(c.Id, "B"));
        Assert.Equal(PartyResult.NotLeader, _w.Parties.Invite(b.Id, "C"));
    }

    [Fact]
    public void Refuse_an_invite_into_a_full_party()
    {
        var w = new PartyTestWorld(c => c.MaxPartySize = 2);
        PartyClient a = w.Online(1, "A");
        PartyClient b = w.Online(2, "B");
        w.Online(3, "C");
        w.Form(a, b);

        Assert.Equal(PartyResult.PartyFull, w.Parties.Invite(a.Id, "C"));
    }

    [Fact]
    public void Refuse_an_accept_into_a_party_that_filled_meanwhile()
    {
        var w = new PartyTestWorld(c => c.MaxPartySize = 2);
        PartyClient a = w.Online(1, "A");
        PartyClient b = w.Online(2, "B");
        PartyClient c = w.Online(3, "C");
        w.Form(a, b);
        w.Parties.Leave(b.Id);                     // disbands: A alone, no party
        Assert.Equal(PartyResult.Ok, w.Parties.Invite(a.Id, "B"));
        Assert.Equal(PartyResult.Ok, w.Parties.Invite(a.Id, "C"));
        Assert.Equal(PartyResult.Ok, w.Parties.Respond(b.Id, accept: true));

        Assert.Equal(PartyResult.PartyFull, w.Parties.Respond(c.Id, accept: true));
    }

    [Fact]
    public void Expire_an_invite_and_tell_both_sides()
    {
        PartyClient a = _w.Online(1, "A");
        PartyClient b = _w.Online(2, "B");
        _w.Parties.Invite(a.Id, "B");

        _w.Clock.Advance(TimeSpan.FromSeconds(59));
        _w.Parties.Tick();
        Assert.Empty(a.Results());

        _w.Clock.Advance(TimeSpan.FromSeconds(1));
        _w.Parties.Tick();

        SPartyResultPacket toInviter = Assert.Single(a.Results());
        Assert.Equal(PartyResult.InviteExpired, toInviter.Result);
        Assert.Equal("B", toInviter.Name);
        Assert.Equal(PartyResult.InviteExpired, Assert.Single(b.Results()).Result);
        Assert.Equal(PartyResult.NoInvite, _w.Parties.Respond(b.Id, accept: true));
    }

    [Fact]
    public void Tell_the_inviter_of_a_decline()
    {
        PartyClient a = _w.Online(1, "A");
        PartyClient b = _w.Online(2, "B");
        _w.Parties.Invite(a.Id, "B");

        Assert.Equal(PartyResult.Ok, _w.Parties.Respond(b.Id, accept: false));

        SPartyResultPacket told = Assert.Single(a.Results());
        Assert.Equal(PartyResult.InviteDeclined, told.Result);
        Assert.Equal("B", told.Name);
        Assert.Null(_w.Parties.PartyOf(a.Id));
    }

    [Fact]
    public void Disband_a_party_that_falls_below_two()
    {
        PartyClient a = _w.Online(1, "A");
        PartyClient b = _w.Online(2, "B");
        _w.Form(a, b);
        Party party = _w.Parties.PartyOf(a.Id)!;

        Assert.Equal(PartyResult.Ok, _w.Parties.Leave(b.Id));

        Assert.Null(_w.Parties.PartyOf(a.Id));
        Assert.Null(_w.Parties.PartyOf(b.Id));
        Assert.Null(a.Character.PartyId);
        Assert.Empty(a.Rosters().Last().Members);
        Assert.Empty(b.Rosters().Last().Members);
        Assert.Contains("The party was disbanded.", a.Lines());
        Assert.Contains(party.Id, _w.Instances.Forgotten);
    }

    [Fact]
    public void Hand_leadership_to_the_longest_standing_online_member_when_the_leader_leaves()
    {
        PartyClient a = _w.Online(1, "A");
        PartyClient b = _w.Online(2, "B");
        PartyClient c = _w.Online(3, "C");
        _w.Form(a, b, c);

        _w.Parties.Leave(a.Id);

        Party party = _w.Parties.PartyOf(b.Id)!;
        Assert.True(party.IsLeader(b.Id));
        Assert.Contains("B is now the party leader.", c.Lines());
        Assert.Equal(["B", "C"], c.Rosters().Last().Members.Select(m => m.Name));
    }

    [Fact]
    public void Let_only_the_leader_kick_and_promote()
    {
        PartyClient a = _w.Online(1, "A");
        PartyClient b = _w.Online(2, "B");
        PartyClient c = _w.Online(3, "C");
        PartyClient stranger = _w.Online(4, "D");
        _w.Form(a, b, c);

        Assert.Equal(PartyResult.NotLeader, _w.Parties.Kick(b.Id, c.Id));
        Assert.Equal(PartyResult.Self, _w.Parties.Kick(a.Id, a.Id));
        Assert.Equal(PartyResult.NotFound, _w.Parties.Kick(a.Id, stranger.Id));
        Assert.Equal(PartyResult.NotInParty, _w.Parties.Kick(stranger.Id, a.Id));
        Assert.Equal(PartyResult.Ok, _w.Parties.Kick(a.Id, c.Id));
        Assert.Contains("C was removed from the party.", b.Lines());
        Assert.Null(_w.Parties.PartyOf(c.Id));

        Assert.Equal(PartyResult.NotLeader, _w.Parties.Promote(b.Id, a.Id));
        Assert.Equal(PartyResult.Self, _w.Parties.Promote(a.Id, a.Id));
        Assert.Equal(PartyResult.Ok, _w.Parties.Promote(a.Id, b.Id));
        Assert.True(_w.Parties.PartyOf(a.Id)!.IsLeader(b.Id));
    }

    [Fact]
    public void Switch_the_experience_mode_at_once_then_hold_it_for_the_cooldown()
    {
        PartyClient a = _w.Online(1, "A");
        PartyClient b = _w.Online(2, "B");
        _w.Form(a, b);

        Assert.Equal(PartyResult.Invalid, _w.Parties.SetExperienceMode(a.Id, PartyExperienceMode.Unknown));
        Assert.Equal(PartyResult.NotLeader, _w.Parties.SetExperienceMode(b.Id, PartyExperienceMode.LevelWeighted));
        Assert.Equal(PartyResult.Ok, _w.Parties.SetExperienceMode(a.Id, PartyExperienceMode.LevelWeighted));
        Assert.Equal(PartyExperienceMode.LevelWeighted, _w.Parties.PartyOf(a.Id)!.ExperienceMode);
        Assert.Equal(60_000u, b.Rosters().Last().ModeLockedForMs);
        Assert.Contains("Experience is now shared by level.", b.Lines());

        _w.Clock.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal(PartyResult.OnCooldown, _w.Parties.SetExperienceMode(a.Id, PartyExperienceMode.Even));

        _w.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(PartyResult.Ok, _w.Parties.SetExperienceMode(a.Id, PartyExperienceMode.Even));
    }

    [Fact]
    public void Refuse_a_mode_switch_while_a_member_is_in_combat()
    {
        PartyClient a = _w.Online(1, "A");
        PartyClient b = _w.Online(2, "B");
        _w.Form(a, b);
        b.Character.MarkCombat();

        Assert.Equal(PartyResult.InCombat, _w.Parties.SetExperienceMode(a.Id, PartyExperienceMode.LevelWeighted));
    }

    [Fact]
    public void Hand_leadership_over_when_the_leader_goes_offline_and_keep_it_when_nobody_is_online()
    {
        PartyClient a = _w.Online(1, "A");
        PartyClient b = _w.Online(2, "B");
        PartyClient c = _w.Online(3, "C");
        _w.Form(a, b, c);

        _w.Parties.CharacterOffline(a.Connection, a.Character);
        Assert.True(_w.Parties.PartyOf(b.Id)!.IsLeader(b.Id));
        Assert.False(b.Rosters().Last().Members.Single(m => m.Name == "A").Online);

        _w.Parties.CharacterOffline(c.Connection, c.Character);
        _w.Parties.CharacterOffline(b.Connection, b.Character);
        Assert.True(_w.Parties.PartyOf(b.Id)!.IsLeader(b.Id)); // nobody online: it stays

        PartyClient cAgain = _w.Online(3, "C");
        _w.Parties.Tick();
        Assert.True(_w.Parties.PartyOf(c.Id)!.IsLeader(c.Id)); // the leader is offline and C is online
        Assert.Equal(_w.Parties.PartyOf(c.Id)!.Id, cAgain.Character.PartyId);
    }

    [Fact]
    public void End_the_invites_of_a_character_who_goes_offline()
    {
        PartyClient a = _w.Online(1, "A");
        PartyClient b = _w.Online(2, "B");
        PartyClient c = _w.Online(3, "C");
        _w.Parties.Invite(a.Id, "B");
        _w.Parties.Invite(c.Id, "A");

        _w.Parties.CharacterOffline(a.Connection, a.Character);

        Assert.Equal(PartyResult.InviteExpired, Assert.Single(b.Results()).Result);
        Assert.Equal(PartyResult.NoInvite, _w.Parties.Respond(b.Id, accept: true));
        Assert.Equal(PartyResult.InviteExpired, Assert.Single(c.Results()).Result);
    }

    [Fact]
    public void Keep_a_member_offline_in_the_party_and_send_the_roster_when_it_returns()
    {
        PartyClient a = _w.Online(1, "A");
        PartyClient b = _w.Online(2, "B");
        _w.Form(a, b);
        _w.Parties.CharacterOffline(b.Connection, b.Character);

        PartyClient back = _w.Online(2, "B");

        Assert.NotNull(_w.Parties.PartyOf(2));
        Assert.All(back.Rosters().Last().Members, m => Assert.True(m.Online));
    }
}
