using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Repositories;

public interface IMapPortalRepository
{
    Task<IReadOnlyList<MapPortal>> FindAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MapPortal>> FindBySourceMapAsync(ushort sourceMapId, CancellationToken cancellationToken = default);
}

public class MapPortalRepository(WorldDbContext dbContext) : IMapPortalRepository
{
    public async Task<IReadOnlyList<MapPortal>> FindAllAsync(CancellationToken cancellationToken = default)
        => await dbContext.MapPortals.AsNoTracking().ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MapPortal>> FindBySourceMapAsync(ushort sourceMapId, CancellationToken cancellationToken = default)
        => await dbContext.MapPortals.AsNoTracking()
            .Where(p => p.SourceMapId == sourceMapId)
            .ToListAsync(cancellationToken);
}
