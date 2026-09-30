using Avalon.Network.Packets.Party;
using Avalon.Server.World.UnitTests.Parties;
using Avalon.World.Handlers;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Handlers;

public class PartyHandlersShould
{
    private readonly PartyTestWorld _w = new();

    [Fact]
    public void Answer_each_request_exactly_once()
    {
        PartyClient a = _w.Online(1, "A");
        PartyClient b = _w.Online(2, "B");

        new PartyInviteHandler(_w.Parties).Execute(a.Connection, new CPartyInvitePacket { TargetName = "B" });
        new PartyInviteResponseHandler(_w.Parties).Execute(b.Connection, new CPartyInviteResponsePacket { Accept = true });
        new PartyExperienceModeHandler(_w.Parties).Execute(a.Connection, new CPartyExperienceModePacket { Mode = PartyExperienceMode.LevelWeighted });
        new PartyPromoteHandler(_w.Parties).Execute(a.Connection, new CPartyPromotePacket { CharacterId = b.Id });
        new PartyKickHandler(_w.Parties).Execute(a.Connection, new CPartyKickPacket { CharacterId = b.Id });
        new PartyLeaveHandler(_w.Parties).Execute(b.Connection, new CPartyLeavePacket());

        Assert.Equal([PartyResult.Ok, PartyResult.Ok, PartyResult.Ok, PartyResult.NotLeader],
            a.Results().Select(r => r.Result));
        Assert.Equal([PartyResult.Ok, PartyResult.Ok], b.Results().Select(r => r.Result));
        Assert.Equal("B", a.Results()[0].Name);
    }

    [Fact]
    public void Answer_nothing_for_a_connection_with_no_character()
    {
        PartyClient a = _w.Online(1, "A");
        a.Connection.Character.Returns((Avalon.World.Public.Characters.ICharacter?)null);

        new PartyLeaveHandler(_w.Parties).Execute(a.Connection, new CPartyLeavePacket());

        Assert.Empty(a.Results());
    }
}
