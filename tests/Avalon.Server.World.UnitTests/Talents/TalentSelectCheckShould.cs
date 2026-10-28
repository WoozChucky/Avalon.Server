using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.World;
using Avalon.World.Persistence;
using Avalon.World.Talents;
using Avalon.World.Testing.Scenarios;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Server.World.UnitTests.Talents;

/// <summary>
/// #1181: a Warrior's saved talents are checked at select against the live tree, and only what no longer fits is dropped,
/// each drop marked for the save. Over the seeded tree: the Warrior's start is node 1, and lane A runs 2, 3, 4, 5, 6
/// (Cleave), 7 from it; node 74 is the Hunter's start. A tree the catalog refused drops nothing.
/// </summary>
public class TalentSelectCheckShould
{
    private static StaticData Data => ScenarioReferenceData.Shared.Data;

    [Theory]
    [InlineData("all fit", new[] { 2, 3, 4, 5, 6 }, 6, new[] { 2, 3, 4, 5, 6 })]
    [InlineData("unknown node", new[] { 2, 999 }, 6, new[] { 2 })]
    [InlineData("cut off", new[] { 2, 3, 4, 5 }, 6, new[] { 2 })]
    [InlineData("start held", new[] { 2, 74 }, 6, new[] { 2 })]
    [InlineData("over budget", new[] { 2, 3, 4, 5, 6, 7 }, 5, new int[0])]
    [InlineData("tree refused", new[] { 2, 3, 999 }, 1, new[] { 2, 3, 999 })]
    public void Drop_only_what_no_longer_fits(string row, int[] held, int earned, int[] kept)
    {
        TalentCatalog tree = row switch
        {
            "cut off" => new TalentCatalog(Without(TalentSeed(), 3), Data.Abilities, Data.Auras,
                NullLoggerFactory.Instance),
            "tree refused" => new TalentCatalog(new TalentTreeRows([], [], [], [], [], []), Data.Abilities, Data.Auras,
                NullLoggerFactory.Instance),
            _ => Data.Talents,
        };
        Assert.Equal(row != "tree refused", tree.IsLoaded);
        var save = new SaveStateTracker();
        var talents = new TalentAllocation(save);
        talents.Load([.. held.Select(n => new CharacterTalent { CharacterId = 118_102u, NodeId = n })]);

        TalentSelectCheck.Apply(talents, tree, CharacterClass.Warrior, earned, new CharacterId(118_102u),
            NullLogger.Instance);

        Assert.Equal(kept, talents.Held);
        Assert.Equal(held.Except(kept).Order(), save.TakeMarks().Talents!.Keys.Order());
    }

    private static TalentTreeRows TalentSeed() => Avalon.Database.World.Seeding.TalentSeed.Rows();

    /// <summary>The rows with <paramref name="nodeId" />, its edges, its lines and its link gone: a later edit's tree.</summary>
    private static TalentTreeRows Without(TalentTreeRows rows, int nodeId) => rows with
    {
        Nodes = [.. rows.Nodes.Where(n => n.Id != nodeId)],
        Edges = [.. rows.Edges.Where(e => e.NodeA != nodeId && e.NodeB != nodeId)],
        Stats = [.. rows.Stats.Where(s => s.NodeId != nodeId)],
        Abilities = [.. rows.Abilities.Where(a => a.NodeId != nodeId)],
    };
}
