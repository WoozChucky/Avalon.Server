using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Repositories;

public interface IMapCreatureSpawnRepository
{
    /// <summary>
    /// The authored creature spawns for one map, or an empty collection when it has none — which is
    /// the case for every procedural map today. Each spawn's <see cref="MapCreatureSpawn.Path"/> is
    /// loaded with its points, because placement turns it into the creature's patrol route.
    /// </summary>
    Task<IReadOnlyCollection<MapCreatureSpawn>> FindByMapAsync(
        MapTemplateId mapTemplateId, CancellationToken cancellationToken = default);
}

public class MapCreatureSpawnRepository(IDbContextFactory<WorldDbContext> contextFactory)
    : IMapCreatureSpawnRepository
{
    public async Task<IReadOnlyCollection<MapCreatureSpawn>> FindByMapAsync(
        MapTemplateId mapTemplateId, CancellationToken cancellationToken = default)
    {
        await using WorldDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.MapCreatureSpawns
            .AsNoTracking()
            .Include(s => s.Path)
            .ThenInclude(p => p!.Points)
            .Where(s => s.MapTemplateId == mapTemplateId)
            .OrderBy(s => s.Id)
            .ToListAsync(cancellationToken);
    }
}
