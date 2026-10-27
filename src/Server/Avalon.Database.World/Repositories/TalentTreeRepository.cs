using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Repositories;

/// <summary>The talent tree's six tables (#1181).</summary>
public interface ITalentTreeRepository
{
    /// <summary>The whole tree, untracked, each table by its key: what the world's talent catalog checks and holds.</summary>
    Task<TalentTreeRows> GetTreeAsync(CancellationToken ct = default);

    /// <summary>Whether a start or a node links the ability: what makes it a granted ability.</summary>
    Task<bool> IsGrantedAsync(AbilityId abilityId, CancellationToken ct = default);

    /// <summary>
    /// The rows a character's saved nodes read, untracked: the nodes among <paramref name="nodeIds" /> the tree still
    /// holds and their stat lines, <paramref name="characterClass" />'s start, and the ability links of those nodes and
    /// of that start. No edges and no fork groups: what the REST character reads (#1181) need, not the whole tree.
    /// </summary>
    Task<TalentTreeRows> GetHeldAsync(CharacterClass characterClass, IReadOnlyCollection<int> nodeIds,
        CancellationToken ct = default);
}

public class TalentTreeRepository(IDbContextFactory<WorldDbContext> contextFactory) : ITalentTreeRepository
{
    public async Task<TalentTreeRows> GetTreeAsync(CancellationToken ct = default)
    {
        await using WorldDbContext context = await contextFactory.CreateDbContextAsync(ct);

        List<TalentNode> nodes = await context.TalentNodes.AsNoTracking().OrderBy(n => n.Id).ToListAsync(ct);
        List<TalentEdge> edges = await context.TalentEdges.AsNoTracking()
            .OrderBy(e => e.NodeA).ThenBy(e => e.NodeB).ToListAsync(ct);
        List<TalentNodeStat> stats = await context.TalentNodeStats.AsNoTracking()
            .OrderBy(s => s.NodeId).ThenBy(s => s.Stat).ThenBy(s => s.Kind).ToListAsync(ct);
        List<TalentNodeAbility> abilities = await context.TalentNodeAbilities.AsNoTracking()
            .OrderBy(a => a.NodeId).ToListAsync(ct);
        List<TalentForkGroup> forkGroups = await context.TalentForkGroups.AsNoTracking().OrderBy(g => g.Id).ToListAsync(ct);
        List<TalentClassStart> starts = await context.TalentClassStarts.AsNoTracking().OrderBy(s => s.Class).ToListAsync(ct);

        return new TalentTreeRows(nodes, edges, stats, abilities, forkGroups, starts);
    }

    public async Task<bool> IsGrantedAsync(AbilityId abilityId, CancellationToken ct = default)
    {
        await using WorldDbContext context = await contextFactory.CreateDbContextAsync(ct);

        return await context.TalentNodeAbilities.AsNoTracking().AnyAsync(a => a.AbilityId == abilityId, ct);
    }

    public async Task<TalentTreeRows> GetHeldAsync(CharacterClass characterClass, IReadOnlyCollection<int> nodeIds,
        CancellationToken ct = default)
    {
        await using WorldDbContext context = await contextFactory.CreateDbContextAsync(ct);

        // var, not an explicit type: an EF Core query captures these arrays, and an explicit non-nullable array type
        // makes the compiler add a Convert node to the expression tree EF Core translates.
#pragma warning disable IDE0008
        var held = nodeIds.Distinct().ToArray();
        List<TalentClassStart> starts = await context.TalentClassStarts.AsNoTracking()
            .Where(s => s.Class == characterClass).ToListAsync(ct);
        var linked = held.Concat(starts.Select(s => s.NodeId)).ToArray();
#pragma warning restore IDE0008

        List<TalentNode> nodes = await context.TalentNodes.AsNoTracking()
            .Where(n => held.Contains(n.Id)).OrderBy(n => n.Id).ToListAsync(ct);
        List<TalentNodeStat> stats = await context.TalentNodeStats.AsNoTracking()
            .Where(s => held.Contains(s.NodeId)).OrderBy(s => s.NodeId).ThenBy(s => s.Stat).ThenBy(s => s.Kind)
            .ToListAsync(ct);
        List<TalentNodeAbility> abilities = await context.TalentNodeAbilities.AsNoTracking()
            .Where(a => linked.Contains(a.NodeId)).OrderBy(a => a.NodeId).ToListAsync(ct);

        return new TalentTreeRows(nodes, [], stats, abilities, [], starts);
    }
}
