using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Repositories;

public interface IMapPortalRepository
{
    Task<IReadOnlyList<MapPortal>> FindAllAsync(CancellationToken token = default);
    Task<IReadOnlyList<MapPortal>> FindBySourceMapAsync(ushort sourceMapId, CancellationToken token = default);
}

public class MapPortalRepository(WorldDbContext dbContext) : IMapPortalRepository
{
    public async Task<IReadOnlyList<MapPortal>> FindAllAsync(CancellationToken token = default)
        => await dbContext.MapPortals.AsNoTracking().ToListAsync(token);

    public async Task<IReadOnlyList<MapPortal>> FindBySourceMapAsync(ushort sourceMapId, CancellationToken token = default)
        => await dbContext.MapPortals.AsNoTracking()
            .Where(p => p.SourceMapId == sourceMapId)
            .ToListAsync(token);
}
