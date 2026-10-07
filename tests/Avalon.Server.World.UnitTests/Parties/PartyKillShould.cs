using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.World;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Public.Creatures;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Parties;

/// <summary>
/// A real kill through a real party MapInstance (2026-09-30): the experience is shared among the members who share
/// the kill, with the party bonus, and a member the level gap or more above the creature gets nothing and is not
/// counted. The map has no level band, so the shares arrive as split.
/// </summary>
public class PartyKillShould
{
    private readonly PartyTestWorld _w = new();

    private (MapInstance Instance, PartyClient A, PartyClient B) PartyOfTwo(ushort levelA, ushort levelB)
    {
        PartyClient a = _w.Online(1, "A", level: levelA);
        PartyClient b = _w.Online(2, "B", level: levelB);
        _w.Form(a, b);

        IWorld world = MapInstanceClients.NewWorld(ExperienceAwardShould.LoadedStaticData());
        MapInstance instance = TestMapInstances.Build(world, ownerPartyId: _w.Parties.PartyOf(a.Id)!.Id,
            parties: _w.Parties);
        MapInstanceClients.Join(instance, a.Character);
        MapInstanceClients.Join(instance, b.Character);
        a.Character.Position = Vector3.zero;
        b.Character.Position = Vector3.zero;
        return (instance, a, b);
    }

    private static Creature Creature(MapInstance instance, ushort level)
    {
        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, 930_001),
            Metadata = Substitute.For<ICreatureMetadata>(),
            Position = Vector3.zero,
            Level = level,
            Experience = 1000,
        };
        instance.AddCreature(creature);
        return creature;
    }

    [Fact]
    public void Share_the_experience_with_the_party_bonus()
    {
        (MapInstance instance, PartyClient a, PartyClient b) = PartyOfTwo(9, 9);
        using (instance)
        {
            instance.ReportKill(Creature(instance, 9), a.Character);

            Assert.Equal(550ul, a.Character.Experience); // 1000 × 1.1 / 2
            Assert.Equal(550ul, b.Character.Experience);
        }
    }

    [Fact]
    public void Give_a_member_the_level_gap_above_nothing_and_the_other_everything()
    {
        (MapInstance instance, PartyClient a, PartyClient b) = PartyOfTwo(14, 9);
        using (instance)
        {
            instance.ReportKill(Creature(instance, 9), a.Character);

            Assert.Equal(0ul, a.Character.Experience);    // 14 >= 9 + 5: nothing, and not counted
            Assert.Equal(1000ul, b.Character.Experience); // alone, so no bonus
        }
    }

    [Fact]
    public void Give_nothing_for_a_kill_by_a_character_in_a_leave_countdown()
    {
        (MapInstance instance, PartyClient a, PartyClient b) = PartyOfTwo(9, 9);
        using (instance)
        {
            _w.Instances.Owned[instance.InstanceId] = instance.OwnerPartyId!;
            _w.Parties.Leave(a.Id);
            Assert.True(_w.Parties.InCountdown(a.Id));

            instance.ReportKill(Creature(instance, 9), a.Character);

            Assert.Equal(0ul, a.Character.Experience);
            Assert.Equal(0ul, b.Character.Experience);
        }
    }
}
