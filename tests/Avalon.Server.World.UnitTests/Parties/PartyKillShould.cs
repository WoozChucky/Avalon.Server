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
        (MapInstance instance, PartyClient[] members) = PartyOf(levelA, levelB);
        return (instance, members[0], members[1]);
    }

    /// <summary>A party of one member per level, the first its leader, all standing at the origin of the party's instance.</summary>
    private (MapInstance Instance, PartyClient[] Members) PartyOf(params ushort[] levels)
    {
        PartyClient[] members = [.. levels.Select((level, i) => _w.Online((uint)i + 1, ((char)('A' + i)).ToString(), level: level))];
        _w.Form(members[0], members[1..]);

        IWorld world = MapInstanceClients.NewWorld(ExperienceAwardShould.LoadedStaticData());
        MapInstance instance = TestMapInstances.Build(world, ownerPartyId: _w.Parties.PartyOf(members[0].Id)!.Id,
            parties: _w.Parties);
        foreach (PartyClient member in members)
        {
            MapInstanceClients.Join(instance, member.Character);
            member.Character.Position = Vector3.zero;
        }

        return (instance, members);
    }

    private static Creature Creature(MapInstance instance, ushort level, uint id = 930_001)
    {
        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, id),
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

    /// <summary>
    /// One instance's kills share the lists they are worked out in (#890), so each kill must start from none of the
    /// one before: a member who shared the first kill and is out of range of the second gains nothing from it, and the
    /// two who share it split it between the two of them.
    /// </summary>
    [Fact]
    public void Share_each_kill_among_its_own_members_only()
    {
        (MapInstance instance, PartyClient[] members) = PartyOf(9, 9, 9);
        using (instance)
        {
            instance.ReportKill(Creature(instance, 9), members[0].Character);
            Assert.All(members, member => Assert.Equal(400ul, member.Character.Experience)); // 1000 × 1.2 / 3

            members[2].Character.Position = new Vector3(500, 0, 500);
            instance.ReportKill(Creature(instance, 9, id: 930_002), members[0].Character);

            Assert.Equal(950ul, members[0].Character.Experience); // 400 + 1000 × 1.1 / 2
            Assert.Equal(950ul, members[1].Character.Experience);
            Assert.Equal(400ul, members[2].Character.Experience); // out of range of the second kill: nothing from it
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
