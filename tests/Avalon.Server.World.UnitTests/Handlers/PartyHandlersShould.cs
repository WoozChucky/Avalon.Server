using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Party;
using Avalon.Server.World.UnitTests.Parties;
using Avalon.World.Handlers;
using Microsoft.Extensions.Logging.Abstractions;
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

        new PartyInviteHandler(_w.Parties, NullLogger<PartyInviteHandler>.Instance).Execute(a.Connection, new CPartyInvitePacket { TargetName = "B" });
        new PartyInviteResponseHandler(_w.Parties, NullLogger<PartyInviteResponseHandler>.Instance).Execute(b.Connection, new CPartyInviteResponsePacket { Accept = true });
        new PartyExperienceModeHandler(_w.Parties, NullLogger<PartyExperienceModeHandler>.Instance).Execute(a.Connection, new CPartyExperienceModePacket { Mode = PartyExperienceMode.LevelWeighted });
        new PartyPromoteHandler(_w.Parties, NullLogger<PartyPromoteHandler>.Instance).Execute(a.Connection, new CPartyPromotePacket { CharacterId = b.Id });
        new PartyKickHandler(_w.Parties, NullLogger<PartyKickHandler>.Instance).Execute(a.Connection, new CPartyKickPacket { CharacterId = b.Id });
        new PartyLeaveHandler(_w.Parties, NullLogger<PartyLeaveHandler>.Instance).Execute(b.Connection, new CPartyLeavePacket());

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

        new PartyLeaveHandler(_w.Parties, NullLogger<PartyLeaveHandler>.Instance).Execute(a.Connection, new CPartyLeavePacket());

        Assert.Empty(a.Results());
    }

    [Fact]
    public void Answer_Error_once_when_the_request_throws()
    {
        PartyClient a = _w.Online(1, "A");
        PartyClient b = _w.Online(2, "B");
        // Sending B its invite throws inside PartyService.Invite, after the invite was recorded.
        b.Connection.When(c => c.Send(Arg.Any<NetworkPacket>())).Do(_ => throw new InvalidOperationException("send failed"));

        new PartyInviteHandler(_w.Parties, NullLogger<PartyInviteHandler>.Instance)
            .Execute(a.Connection, new CPartyInvitePacket { TargetName = "B" });

        SPartyResultPacket result = Assert.Single(a.Results());
        Assert.Equal(PartyResult.Error, result.Result);
        Assert.Null(result.Name);
    }

    [Fact]
    public void Not_answer_twice_when_sending_the_answer_throws()
    {
        PartyClient a = _w.Online(1, "A");
        int sends = 0;
        a.Connection.When(c => c.Send(Arg.Any<NetworkPacket>())).Do(_ =>
        {
            sends++;
            throw new InvalidOperationException("send failed");
        });

        Assert.Throws<InvalidOperationException>(() =>
            new PartyLeaveHandler(_w.Parties, NullLogger<PartyLeaveHandler>.Instance)
                .Execute(a.Connection, new CPartyLeavePacket()));

        Assert.Equal(1, sends);
    }
}
