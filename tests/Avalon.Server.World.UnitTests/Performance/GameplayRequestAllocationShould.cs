using Avalon.Network.Packets.Party;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Parties;
using Avalon.World.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.Performance;

/// <summary>
/// A request a client can send as fast as its connection is read is answered without allocating (#883): no scenario
/// sends one, so the scenario gate would not see a closure come back.
/// </summary>
/// <remarks>
/// Every path here sends its answer, so these run where <see cref="InventoryAllocationShould" /> does and read the
/// fewest of three windows, for the reason given there.
/// </remarks>
[Collection(nameof(ScenarioAllocations))]
public class GameplayRequestAllocationShould
{
    private const int Rounds = 100;

    private readonly CharacterEntity _character = New();
    private readonly QuietConnection _connection;

    public GameplayRequestAllocationShould() => _connection = new QuietConnection(_character);

    [Fact]
    public void Answer_a_party_request_allocating_nothing()
    {
        var parties = new PartyService(Options.Create(new GameConfiguration()), TimeProvider.System,
            NullLogger<PartyService>.Instance);
        var invite = new PartyInviteHandler(parties, NullLogger<PartyInviteHandler>.Instance);
        var respond = new PartyInviteResponseHandler(parties, NullLogger<PartyInviteResponseHandler>.Instance);
        var kick = new PartyKickHandler(parties, NullLogger<PartyKickHandler>.Instance);
        var promote = new PartyPromoteHandler(parties, NullLogger<PartyPromoteHandler>.Instance);
        var leave = new PartyLeaveHandler(parties, NullLogger<PartyLeaveHandler>.Instance);
        var mode = new PartyExperienceModeHandler(parties, NullLogger<PartyExperienceModeHandler>.Instance);

        // Each is refused (a name that is none, no invite, no party), which is what a client can repeat for ever.
        var nobody = new CPartyInvitePacket { TargetName = "no such name" };
        var yes = new CPartyInviteResponsePacket { Accept = true };
        var out9 = new CPartyKickPacket { CharacterId = 9 };
        var up9 = new CPartyPromotePacket { CharacterId = 9 };
        var gone = new CPartyLeavePacket();
        var even = new CPartyExperienceModePacket { Mode = PartyExperienceMode.Even };

        long bytes = ThreadAllocations.FewestOfThreeWindows(Rounds, () =>
        {
            invite.Execute(_connection, nobody);
            respond.Execute(_connection, yes);
            kick.Execute(_connection, out9);
            promote.Execute(_connection, up9);
            leave.Execute(_connection, gone);
            mode.Execute(_connection, even);
        });

        Assert.Equal(0, bytes);
        Assert.Equal(6 * 4 * Rounds, _connection.Sent);
    }
}
