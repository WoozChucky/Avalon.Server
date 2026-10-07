using Avalon.World.Public;

namespace Avalon.Server.World.UnitTests.Parties;

public class PartyLeaveCountdownShould
{
    private static readonly Guid PartyInstance = Guid.NewGuid();
    private readonly PartyTestWorld _w = new();
    private readonly PartyClient _a;
    private readonly PartyClient _b;
    private readonly PartyClient _c;

    public PartyLeaveCountdownShould()
    {
        _a = _w.Online(1, "A", instance: PartyInstance);
        _b = _w.Online(2, "B", instance: PartyInstance);
        _c = _w.Online(3, "C", instance: PartyInstance);
        _w.Form(_a, _b, _c);
        _w.Instances.Owned[PartyInstance] = _w.Parties.PartyOf(_a.Id)!.Id;
    }

    private IReadOnlyList<IWorldConnection> After(int seconds)
    {
        _w.Clock.Advance(TimeSpan.FromSeconds(seconds));
        return _w.Parties.Tick().ToList();
    }

    [Fact]
    public void Count_down_out_loud_and_hand_the_member_over_at_the_deadline()
    {
        _w.Parties.Leave(_c.Id);

        Assert.True(_w.Parties.InCountdown(_c.Id));
        Assert.Contains("You left the party. Returning to town in 60 seconds.", _c.Lines());
        _c.Clear();

        Assert.Empty(After(30));
        Assert.Equal(["Returning to town in 30 seconds."], _c.Lines());
        Assert.Empty(After(20));
        Assert.Empty(After(5));
        Assert.Empty(After(1));
        Assert.Empty(After(1));
        Assert.Empty(After(1));
        Assert.Empty(After(1));
        Assert.Equal(
            ["Returning to town in 30 seconds.", "Returning to town in 10 seconds.", "Returning to town in 5 seconds.",
             "Returning to town in 4 seconds.", "Returning to town in 3 seconds.", "Returning to town in 2 seconds.",
             "Returning to town in 1 second."],
            _c.Lines());

        Assert.Same(_c.Connection, Assert.Single(After(1)));
        Assert.False(_w.Parties.InCountdown(_c.Id));
    }

    [Fact]
    public void Give_a_kicked_member_a_countdown_too()
    {
        _w.Parties.Kick(_a.Id, _b.Id);

        Assert.True(_w.Parties.InCountdown(_b.Id));
        Assert.Contains("You were removed from the party. Returning to town in 60 seconds.", _b.Lines());
    }

    [Fact]
    public void Give_the_last_member_a_countdown_when_the_party_disbands()
    {
        _w.Parties.Leave(_c.Id);
        _w.Parties.Leave(_b.Id);

        Assert.True(_w.Parties.InCountdown(_a.Id));
        Assert.Contains("The party was disbanded. Returning to town in 60 seconds.", _a.Lines());
    }

    [Fact]
    public void Start_no_countdown_outside_the_party_instance()
    {
        _c.Character.InstanceId = Guid.NewGuid();

        _w.Parties.Leave(_c.Id);

        Assert.False(_w.Parties.InCountdown(_c.Id));
    }

    [Fact]
    public void Cancel_the_countdown_on_a_re_invite_to_the_same_party()
    {
        _w.Parties.Leave(_c.Id);
        _w.Parties.Invite(_a.Id, "C");
        _w.Parties.Respond(_c.Id, accept: true);

        Assert.False(_w.Parties.InCountdown(_c.Id));
        Assert.Empty(After(60));
    }

    [Fact]
    public void Drop_the_countdown_of_a_member_who_leaves_the_instance()
    {
        _w.Parties.Leave(_c.Id);
        _c.Character.InstanceId = Guid.NewGuid(); // a portal, or a respawn in town

        Assert.Empty(After(60));
        Assert.False(_w.Parties.InCountdown(_c.Id));
    }

    [Fact]
    public void Drop_the_countdown_of_a_member_who_logs_out()
    {
        _w.Parties.Leave(_c.Id);
        _w.Parties.CharacterOffline(_c.Connection, _c.Character);

        Assert.False(_w.Parties.InCountdown(_c.Id));
        Assert.Empty(After(60));
    }
}
