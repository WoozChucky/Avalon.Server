using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Network.Packets.Talents;
using Avalon.Server.World.UnitTests.Abilities;
using Avalon.World.Abilities;
using Avalon.World.Persistence;
using Avalon.World.Talents;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Server.World.UnitTests.Talents;

/// <summary>
/// #1181: an allocation is all or nothing, and the rules only decide. A request is refused at the first rule it breaks,
/// naming the node, in the order dead, too many, then each node as sent: unknown, already allocated, not connected, fork
/// locked. The tree here: the Warrior's start 1, a chain 1-2-3-4, a branch 2-5, a fork pair of capstones 6 and 7 off 4
/// (fork group 1), and the Hunter's start 10 joined at 4-10, with the Wizard's start 11 and the Cleric's 12 beyond it:
/// ten nodes.
/// </summary>
public class TalentRulesShould
{
    private static readonly TalentCatalog s_tree = Tree();

    [Theory]
    [InlineData("next to the start", new uint[0], new uint[] { 2 }, 5, false, TalentResult.Ok, 0u)]
    [InlineData("a batch building on itself", new uint[0], new uint[] { 2, 3, 4 }, 5, false, TalentResult.Ok, 0u)]
    [InlineData("not adjacent", new uint[0], new uint[] { 3 }, 5, false, TalentResult.NotConnected, 3u)]
    [InlineData("fourth not connected", new uint[0], new uint[] { 2, 3, 5, 7 }, 5, false, TalentResult.NotConnected, 7u)]
    [InlineData("listed twice", new uint[0], new uint[] { 2, 3, 2 }, 5, false, TalentResult.AlreadyAllocated, 2u)]
    [InlineData("held", new uint[] { 2 }, new uint[] { 2 }, 5, false, TalentResult.AlreadyAllocated, 2u)]
    [InlineData("a start", new uint[0], new uint[] { 10 }, 5, false, TalentResult.AlreadyAllocated, 10u)]
    [InlineData("unknown", new uint[0], new uint[] { 2, 99 }, 5, false, TalentResult.UnknownNode, 99u)]
    [InlineData("zero", new uint[0], new uint[] { 0 }, 5, false, TalentResult.UnknownNode, 0u)]
    [InlineData("fork side held", new uint[] { 2, 3, 4, 6 }, new uint[] { 7 }, 5, false, TalentResult.ForkLocked, 7u)]
    [InlineData("fork side earlier", new uint[] { 2, 3, 4 }, new uint[] { 6, 7 }, 5, false, TalentResult.ForkLocked, 7u)]
    [InlineData("more than available", new uint[] { 2 }, new uint[] { 3, 4, 5 }, 3, false, TalentResult.TooMany, 0u)]
    [InlineData("more than the tree", new uint[0], new uint[] { 2, 3, 4, 5, 6, 7, 2, 3, 4, 5, 6, 7 }, 50, false,
        TalentResult.TooMany, 0u)]
    [InlineData("dead", new uint[0], new uint[] { 2 }, 5, true, TalentResult.Dead, 0u)]
    [InlineData("empty", new uint[0], new uint[0], 5, false, TalentResult.Ok, 0u)]
    public void Allocate_or_refuse_naming_the_node(string row, uint[] held, uint[] request, int earned, bool dead,
        TalentResult result, uint node)
    {
        Assert.True(s_tree.IsLoaded, s_tree.Refusal);
        var save = new SaveStateTracker();
        var talents = new TalentAllocation(save);
        talents.Load([.. held.Select(n => new CharacterTalent { CharacterId = 118_101u, NodeId = (int)n })]);
        int version = talents.Version;

        TalentResult decided = TalentRules.Allocate(talents, s_tree, CharacterClass.Warrior, dead, earned, request,
            out uint refused);

        Assert.True((result, node) == (decided, refused), $"{row}: {decided} naming {refused}");
        Assert.Equal(held.Select(n => (int)n), talents.Held);
        Assert.Equal(version, talents.Version);
        Assert.Null(save.TakeMarks().Talents);
    }

    private static TalentCatalog Tree()
    {
        List<TalentNode> nodes =
        [
            new() { Id = 1, Kind = TalentNodeKind.Start, Name = "Warrior start" },
            new() { Id = 2, Kind = TalentNodeKind.Small },
            new() { Id = 3, Kind = TalentNodeKind.Small },
            new() { Id = 4, Kind = TalentNodeKind.Small },
            new() { Id = 5, Kind = TalentNodeKind.Small },
            new() { Id = 6, Kind = TalentNodeKind.Capstone, Name = "Left capstone", ForkGroupId = 1 },
            new() { Id = 7, Kind = TalentNodeKind.Capstone, Name = "Right capstone", ForkGroupId = 1 },
            new() { Id = 10, Kind = TalentNodeKind.Start, Name = "Hunter start" },
            new() { Id = 11, Kind = TalentNodeKind.Start, Name = "Wizard start" },
            new() { Id = 12, Kind = TalentNodeKind.Start, Name = "Cleric start" },
        ];
        List<TalentEdge> edges =
        [
            Edge(1, 2), Edge(2, 3), Edge(3, 4), Edge(2, 5), Edge(4, 6), Edge(4, 7), Edge(4, 10), Edge(10, 11), Edge(11, 12),
        ];
        List<TalentNodeAbility> links = [Link(1, 100), Link(10, 101), Link(11, 102), Link(12, 103)];
        List<TalentClassStart> starts =
        [
            new() { Class = CharacterClass.Warrior, NodeId = 1 }, new() { Class = CharacterClass.Hunter, NodeId = 10 },
            new() { Class = CharacterClass.Wizard, NodeId = 11 }, new() { Class = CharacterClass.Cleric, NodeId = 12 },
        ];
        List<AbilityTemplate> abilities =
            [AbilityTestData.Cone(100), AbilityTestData.Cone(101), AbilityTestData.Cone(102), AbilityTestData.Cone(103)];

        return new TalentCatalog(new TalentTreeRows(nodes, edges, [], links, [new TalentForkGroup { Id = 1 }], starts),
            new AbilityCatalog(abilities, NullLoggerFactory.Instance), NullLoggerFactory.Instance);

        static TalentEdge Edge(int a, int b) => new() { NodeA = a, NodeB = b };
        static TalentNodeAbility Link(int nodeId, uint abilityId) => new() { NodeId = nodeId, AbilityId = new AbilityId(abilityId) };
    }
}
