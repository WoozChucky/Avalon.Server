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

public class MapChunkPlacementRepository : IMapChunkPlacementRepository
{
    private readonly WorldDbContext _ctx;

    public MapChunkPlacementRepository(WorldDbContext ctx) => _ctx = ctx;

    public async Task<IReadOnlyList<MapChunkPlacement>> FindByMapAsync(MapTemplateId mapId, CancellationToken ct = default)
    {
        return await _ctx.MapChunkPlacements
            .AsNoTracking()
            .Where(p => p.MapTemplateId == mapId)
            .OrderBy(p => p.GridZ)
            .ThenBy(p => p.GridX)
            .ToListAsync(ct);
    }

    public async Task ReplaceForMapAsync(MapTemplateId mapId, IReadOnlyList<MapChunkPlacement> placements,
        CancellationToken ct = default)
    {
        await using var tx = await _ctx.Database.BeginTransactionAsync(ct);

        var existing = await _ctx.MapChunkPlacements
            .Where(p => p.MapTemplateId == mapId)
            .ToListAsync(ct);
        _ctx.MapChunkPlacements.RemoveRange(existing);
        await _ctx.SaveChangesAsync(ct);

        await _ctx.MapChunkPlacements.AddRangeAsync(placements, ct);
        await _ctx.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
    }
}
