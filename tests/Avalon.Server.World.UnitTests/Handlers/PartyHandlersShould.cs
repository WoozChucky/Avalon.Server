using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Party;
using Avalon.Server.World.UnitTests.Parties;
using Avalon.World.Handlers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Handlers;

public class PartyHandlersShould
{
    private readonly PartyTestWorld _w = new();

    [Fact]
    public void Answer_each_request_exactly_once()
    {
        PartyClient a = _w.Online(1, "Aren");
        PartyClient b = _w.Online(2, "Borin");

        new PartyInviteHandler(_w.Parties, NullLogger<PartyInviteHandler>.Instance).Execute(a.Connection, new CPartyInvitePacket { TargetName = "Borin" });
        new PartyInviteResponseHandler(_w.Parties, NullLogger<PartyInviteResponseHandler>.Instance).Execute(b.Connection, new CPartyInviteResponsePacket { Accept = true });
        new PartyExperienceModeHandler(_w.Parties, NullLogger<PartyExperienceModeHandler>.Instance).Execute(a.Connection, new CPartyExperienceModePacket { Mode = PartyExperienceMode.LevelWeighted });
        new PartyPromoteHandler(_w.Parties, NullLogger<PartyPromoteHandler>.Instance).Execute(a.Connection, new CPartyPromotePacket { CharacterId = b.Id });
        new PartyKickHandler(_w.Parties, NullLogger<PartyKickHandler>.Instance).Execute(a.Connection, new CPartyKickPacket { CharacterId = b.Id });
        new PartyLeaveHandler(_w.Parties, NullLogger<PartyLeaveHandler>.Instance).Execute(b.Connection, new CPartyLeavePacket());

        Assert.Equal([PartyResult.Ok, PartyResult.Ok, PartyResult.Ok, PartyResult.NotLeader],
            a.Results().Select(r => r.Result));
        Assert.Equal([PartyResult.Ok, PartyResult.Ok], b.Results().Select(r => r.Result));
        Assert.Equal("Borin", a.Results()[0].Name);
    }

    /// <summary>
    /// #936: a target name that is not a character name (3 to 12 letters, as sent) is answered as a name nobody has,
    /// before anything is looked up: even one a lookup would find invites nobody, and it is not sent back.
    /// </summary>
    [Theory]
    [InlineData(" Kaela ", "Kaela")]    // the lookup ignores surrounding spaces; the rule does not
    [InlineData("Tester2", "Tester2")]  // a digit, so not a name, though a character here holds it
    public void Refuse_an_invite_that_names_no_character_before_looking_anyone_up(string sent, string onlineAs)
    {
        PartyClient a = _w.Online(1, "Aren");
        PartyClient b = _w.Online(2, onlineAs);

        new PartyInviteHandler(_w.Parties, NullLogger<PartyInviteHandler>.Instance)
            .Execute(a.Connection, new CPartyInvitePacket { TargetName = sent });

        SPartyResultPacket result = Assert.Single(a.Results());
        Assert.Equal(PartyResult.NotFound, result.Result);
        Assert.Null(result.Name);
        Assert.Empty(b.Invites());
    }

    [Fact]
    public void Answer_Error_once_when_the_request_throws()
    {
        PartyClient a = _w.Online(1, "Aren");
        PartyClient b = _w.Online(2, "Borin");
        // Sending Borin its invite throws inside PartyService.Invite, after the invite was recorded.
        b.Connection.When(c => c.Send(Arg.Any<OutboundPacket>())).Do(_ => throw new InvalidOperationException("send failed"));

        new PartyInviteHandler(_w.Parties, NullLogger<PartyInviteHandler>.Instance)
            .Execute(a.Connection, new CPartyInvitePacket { TargetName = "Borin" });

        SPartyResultPacket result = Assert.Single(a.Results());
        Assert.Equal(PartyResult.Error, result.Result);
        Assert.Null(result.Name);
    }

    [Fact]
    public void Not_answer_twice_when_sending_the_answer_throws()
    {
        PartyClient a = _w.Online(1, "A");
        int sends = 0;
        a.Connection.When(c => c.Send(Arg.Any<OutboundPacket>())).Do(_ =>
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
