using Avalon.World.Parties;

namespace Avalon.Server.World.UnitTests.Parties;

public class PartyMemberStatusShould
{
    private static readonly Guid s_here = Guid.NewGuid();

    [Fact]
    public void Send_a_changed_status_to_members_in_the_same_instance_only()
    {
        var w = new PartyTestWorld();
        PartyClient a = w.Online(1, "A", instance: s_here);
        PartyClient b = w.Online(2, "B", instance: s_here);
        PartyClient c = w.Online(3, "C", instance: Guid.NewGuid());
        w.Form(a, b, c);
        w.Parties.FlushMemberStatus();       // first snapshot of everyone
        a.Clear(); b.Clear(); c.Clear();

        w.Clock.Advance(PartyService.StatusInterval);
        a.Character.CurrentHealth = 5;
        w.Parties.FlushMemberStatus();

        Assert.Equal(5u, Assert.Single(b.Statuses()).Health);
        Assert.Empty(c.Statuses());          // another instance
        Assert.Empty(a.Statuses());          // never to itself
    }

    [Fact]
    public void Send_at_most_four_times_a_second_per_member()
    {
        var w = new PartyTestWorld();
        PartyClient a = w.Online(1, "A", instance: s_here);
        PartyClient b = w.Online(2, "B", instance: s_here);
        w.Form(a, b);
        w.Parties.FlushMemberStatus();
        b.Clear();

        w.Clock.Advance(PartyService.StatusInterval);
        a.Character.CurrentHealth = 5;
        w.Parties.FlushMemberStatus();
        a.Character.CurrentHealth = 4;
        w.Parties.FlushMemberStatus();       // 0 ms later: held
        Assert.Single(b.Statuses());

        w.Clock.Advance(PartyService.StatusInterval);
        w.Parties.FlushMemberStatus();       // the held change goes now
        Assert.Equal([5u, 4u], b.Statuses().Select(s => s.Health));
    }
}
