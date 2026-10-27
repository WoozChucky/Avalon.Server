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
}
