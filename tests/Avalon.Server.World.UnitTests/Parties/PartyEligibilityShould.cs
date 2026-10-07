using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Parties;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Units;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Parties;

public class PartyEligibilityShould
{
    private static readonly Vector3 Corpse = new(0, 0, 0);

    private readonly PartyTestWorld _w = new();
    private readonly PartyClient _a;
    private readonly PartyClient _b;
    private readonly PartyClient _c;
    private readonly PartyClient _d;

    public PartyEligibilityShould()
    {
        _a = _w.Online(1, "A");
        _b = _w.Online(2, "B");
        _c = _w.Online(3, "C");
        _d = _w.Online(4, "D");
        _w.Form(_a, _b, _c, _d);
        foreach (PartyClient client in new[] { _a, _b, _c, _d })
            client.Character.Position = Corpse;
    }

    private Party Party => _w.Parties.PartyOf(_a.Id)!;

    private static Dictionary<ObjectGuid, ICharacter> Present(params PartyClient[] clients) =>
        clients.ToDictionary(c => c.Character.Guid, c => (ICharacter)c.Character);

    private static IEncounter Encounter(params IUnit[] players)
    {
        IEncounter encounter = Substitute.For<IEncounter>();
        encounter.Players.Returns(players);
        return encounter;
    }

    private static IReadOnlyList<ICharacter> For(ICharacter? killer, Party? party,
        Dictionary<ObjectGuid, ICharacter> present, Func<uint, bool>? inCountdown = null, IEncounter? encounter = null) =>
        PartyEligibility.For(killer, party, present, inCountdown ?? (_ => false), encounter, Corpse, range: 60f);

    [Fact]
    public void Count_a_member_in_the_encounter_or_within_range_and_always_the_killer()
    {
        _a.Character.Position = new Vector3(0, 0, 0);      // the killer
        _b.Character.Position = new Vector3(100, 0, 100);  // 141 m away: counts only through the encounter
        _c.Character.Position = new Vector3(40, 50, 40);   // 56.6 m on X/Z: in range (height ignored)
        _d.Character.Position = new Vector3(70, 0, 0);     // 70 m, not in the encounter: out

        IReadOnlyList<ICharacter> eligible = For(_a.Character, Party, Present(_a, _b, _c, _d),
            encounter: Encounter(_b.Character));

        Assert.Equal([_a.Character, _b.Character, _c.Character], eligible);
    }

    [Fact]
    public void Count_the_killer_however_far_it_is()
    {
        _a.Character.Position = new Vector3(500, 0, 500);

        Assert.Contains(_a.Character, For(_a.Character, Party, Present(_a, _b)));
    }

    [Fact]
    public void Count_a_dead_member()
    {
        _b.Character.IsDead = true;

        Assert.Equal([_a.Character, _b.Character], For(_a.Character, Party, Present(_a, _b)));
    }

    [Fact]
    public void Leave_out_a_member_in_another_instance() =>
        Assert.Equal([_a.Character, _c.Character], For(_a.Character, Party, Present(_a, _c)));

    [Fact]
    public void Leave_out_a_member_in_a_leave_countdown_even_in_the_encounter() =>
        Assert.Equal([_a.Character], For(_a.Character, Party, Present(_a, _b), id => id == _b.Id,
            Encounter(_b.Character)));

    [Fact]
    public void Give_a_killer_with_no_party_the_kill_alone() =>
        Assert.Equal([_a.Character], For(_a.Character, null, Present(_a, _b)));

    [Fact]
    public void Give_nothing_to_a_killer_in_a_leave_countdown() =>
        Assert.Empty(For(_a.Character, null, Present(_a), id => id == _a.Id));

    [Fact]
    public void Give_nothing_to_a_party_killer_in_a_leave_countdown() =>
        Assert.Empty(For(_a.Character, Party, Present(_a, _b), id => id == _a.Id, Encounter(_b.Character)));

    [Fact]
    public void Give_nothing_when_the_killer_is_not_a_character() =>
        Assert.Empty(For(null, Party, Present(_a, _b)));
}
