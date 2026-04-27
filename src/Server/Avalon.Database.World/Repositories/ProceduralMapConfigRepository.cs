using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Repositories;

public interface IProceduralMapConfigRepository
{
    Task<ProceduralMapConfig?> FindByTemplateIdAsync(MapTemplateId id, CancellationToken ct = default);
    Task<IReadOnlyList<ProceduralMapConfig>> FindAllAsync(CancellationToken ct = default);
}

public class ProceduralMapConfigRepository : IProceduralMapConfigRepository
{
    private readonly WorldDbContext _ctx;
    public ProceduralMapConfigRepository(WorldDbContext ctx) => _ctx = ctx;

    public Task<ProceduralMapConfig?> FindByTemplateIdAsync(MapTemplateId id, CancellationToken ct = default)
        => _ctx.ProceduralMapConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.MapTemplateId == id, ct);

    public async Task<IReadOnlyList<ProceduralMapConfig>> FindAllAsync(CancellationToken ct = default)
        => await _ctx.ProceduralMapConfigs.AsNoTracking().ToListAsync(ct);
}
