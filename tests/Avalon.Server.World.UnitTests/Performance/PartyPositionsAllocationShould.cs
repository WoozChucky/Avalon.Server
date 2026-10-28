using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Network.Packets.Party;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Parties;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProtoBuf;

namespace Avalon.Server.World.UnitTests.Performance;

/// <summary>
/// The far party members' positions (#1199) cost the tick nothing once warm: a party of six spread 100 m apart in one
/// instance, every member out of every other's view, sends each member its five far members once a second allocating
/// nothing, and a tick with nothing due allocates nothing either. No scenario spreads a party out of view, so the scenario
/// gate would not see it.
/// </summary>
/// <remarks>In the collection that runs alone, because the lists are sent.</remarks>
[Collection(nameof(ScenarioAllocations))]
public class PartyPositionsAllocationShould
{
    private const int Members = 6;
    private const int Rounds = 100;

    [Fact]
    public void Send_a_spread_partys_positions_and_wait_out_the_second_allocating_nothing()
    {
        var clock = new SteppedClock();
        var parties = new PartyService(Options.Create(new GameConfiguration()), clock, NullLogger<PartyService>.Instance);
        var instance = Guid.NewGuid();
        var characters = new Dictionary<ObjectGuid, ICharacter>();
        var connections = new QuietConnection[Members];
        for (int i = 0; i < Members; i++)
        {
            CharacterEntity character = TestCharacters.New((uint)(1_199_101 + i));
            character.InstanceId = instance;
            character.Position = new Vector3(i * 100f, 0f, 0f);
            characters[character.Guid] = character;
            connections[i] = new QuietConnection(character);
            parties.CharacterOnline(connections[i]);
        }

        for (int i = 1; i < Members; i++)
        {
            Assert.Equal(PartyResult.Ok, parties.Invite(connections[0].Character!.Guid.Id, connections[i].Character!.Name));
            Assert.Equal(PartyResult.Ok, parties.Respond(connections[i].Character!.Guid.Id, accept: true));
        }

        // Each view decided once, as an instance's tick decides it: every member sees itself only.
        var creatures = new Dictionary<ObjectGuid, ICreature>();
        var worldObjects = new List<IWorldObject>();
        var dirty = new Dictionary<ObjectGuid, GameEntityFields>();
        foreach (ICharacter character in characters.Values)
        {
            ((CharacterEntity)character).CharacterGameState.Update(character.Guid, character.Position,
                new InterestRange(60f, 10f), creatures, characters, worldObjects, dirty);
        }

        parties.FlushPositions();   // the first lists, due from the joins
        int sentBefore = connections.Sum(c => c.Sent);

        long sending = ThreadAllocations.FewestOfThreeWindows(Rounds, () =>
        {
            clock.Now += PartyService.PositionsInterval;
            parties.FlushPositions();
        });
        int sent = connections.Sum(c => c.Sent) - sentBefore;

        long before = ThreadAllocations.BeforeQuietWindow();
        for (int i = 0; i < Rounds; i++)
            parties.FlushPositions();
        long waiting = GC.GetAllocatedBytesForCurrentThread() - before;

        // The windows sent what they should: each member, each second, its five far members, and nothing while waiting.
        connections[0].KeepLastPayload = true;
        clock.Now += PartyService.PositionsInterval;
        parties.FlushPositions();
        SPartyPositionsPacket last = Serializer.Deserialize<SPartyPositionsPacket>(connections[0].LastPayload);

        Assert.Multiple(
            () => Assert.Equal(0, sending),
            () => Assert.Equal(0, waiting),
            () => Assert.Equal(4 * Rounds * Members, sent),
            () => Assert.Equal(Members - 1, last.Positions.Count));
    }

    /// <summary>A clock a test moves by hand that allocates nothing to read or move.</summary>
    private sealed class SteppedClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
