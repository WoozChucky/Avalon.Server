using Avalon.Common.ValueObjects;
using Avalon.Domain.World;

namespace Avalon.Database.World.Repositories;

public interface ISpawnTableRepository : IRepository<SpawnTable, SpawnTableId> { }

public class SpawnTableRepository : EntityFrameworkRepository<SpawnTable, SpawnTableId>, ISpawnTableRepository
{
    public SpawnTableRepository(WorldDbContext ctx) : base(ctx) { }
}
