using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Repositories;

public interface ISpawnTableRepository : IRepository<SpawnTable, SpawnTableId> { }

public class SpawnTableRepository(IDbContextFactory<WorldDbContext> contextFactory)
    : EntityFrameworkRepository<SpawnTable, SpawnTableId, WorldDbContext>(contextFactory), ISpawnTableRepository
{
}
