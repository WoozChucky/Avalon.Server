using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Repositories;

public interface IMapDoodadSpawnRepository
{
    /// <summary>
    /// The fixed doodad spawns of one map, by id, or an empty list when it has none (every procedural map today). Read
    /// once per instance build, off the tick.
    /// </summary>
    Task<IReadOnlyList<MapDoodadSpawn>> FindByMapAsync(MapTemplateId map, CancellationToken ct);
}

public class MapDoodadSpawnRepository(IDbContextFactory<WorldDbContext> contextFactory) : IMapDoodadSpawnRepository
{
    public async Task<IReadOnlyList<MapDoodadSpawn>> FindByMapAsync(MapTemplateId map, CancellationToken ct)
    {
        await using WorldDbContext context = await contextFactory.CreateDbContextAsync(ct);

        return await context.MapDoodadSpawns
            .AsNoTracking()
            .Where(s => s.MapTemplateId == map)
            .OrderBy(s => s.Id)
            .ToListAsync(ct);
    }
}
