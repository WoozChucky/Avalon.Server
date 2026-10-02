using Avalon.World.Abilities.Targeting;
using Avalon.World.Public.Enums;
using Xunit;

namespace Avalon.Server.World.UnitTests.Parties;

public class PartyHostilityShould
{
    private static void FlagPvp(PartyClient client) => client.Character.Data!.PvpEnabled = true;

    [Fact]
    public void Never_make_two_members_of_one_party_hostile_whatever_their_flags()
    {
        var w = new PartyTestWorld();
        PartyClient a = w.Online(1, "A");
        PartyClient b = w.Online(2, "B");
        FlagPvp(a);
        FlagPvp(b);
        Assert.True(Hostility.IsHostile(a.Character, b.Character, MapType.Normal)); // flagged strangers

        w.Form(a, b);

        Assert.False(Hostility.IsHostile(a.Character, b.Character, MapType.Normal));
        Assert.True(Hostility.IsAlly(a.Character, b.Character, MapType.Normal)); // so an ally heal reaches them
    }

    [Fact]
    public void Make_former_members_hostile_again_once_the_party_ends()
    {
        var w = new PartyTestWorld();
        PartyClient a = w.Online(1, "A");
        PartyClient b = w.Online(2, "B");
        FlagPvp(a);
        FlagPvp(b);
        w.Form(a, b);

        w.Parties.Leave(b.Id);

        Assert.True(Hostility.IsHostile(a.Character, b.Character, MapType.Normal));
    }
}
