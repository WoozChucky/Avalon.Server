using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Repositories;

public interface IMapChunkPlacementRepository
{
    Task<IReadOnlyList<MapChunkPlacement>> FindByMapAsync(MapTemplateId mapId, CancellationToken ct = default);

    Task ReplaceForMapAsync(MapTemplateId mapId, IReadOnlyList<MapChunkPlacement> placements,
        CancellationToken ct = default);
}

public class MapChunkPlacementRepository(IDbContextFactory<WorldDbContext> contextFactory) : IMapChunkPlacementRepository
{
    public async Task<IReadOnlyList<MapChunkPlacement>> FindByMapAsync(MapTemplateId mapId, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        return await context.MapChunkPlacements
            .AsNoTracking()
            .Where(p => p.MapTemplateId == mapId)
            .OrderBy(p => p.GridZ)
            .ThenBy(p => p.GridX)
            .ToListAsync(ct);
    }

    public async Task ReplaceForMapAsync(MapTemplateId mapId, IReadOnlyList<MapChunkPlacement> placements,
        CancellationToken ct = default)
    {
        // Both saves are in this one method, so the context created here spans the transaction.
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        await using var tx = await context.Database.BeginTransactionAsync(ct);

        var existing = await context.MapChunkPlacements
            .Where(p => p.MapTemplateId == mapId)
            .ToListAsync(ct);
        context.MapChunkPlacements.RemoveRange(existing);
        await context.SaveChangesAsync(ct);

        foreach (var placement in placements)
        {
            context.TrackForInsert(placement);
        }
        await context.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
    }
}
