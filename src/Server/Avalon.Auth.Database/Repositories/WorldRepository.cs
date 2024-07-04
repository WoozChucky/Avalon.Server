using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Auth.Database.Repositories;

public interface IWorldRepository
{
    Task<IList<World>> GetAllAsync();
    Task<World?> FindByIdAsync(int id);
}

public class WorldRepository : IWorldRepository
{
    private AuthDbContext _db;
    
    public WorldRepository(AuthDbContext db)
    {
        _db = db;
    }
    
    public async Task<IList<World>> GetAllAsync()
    {
        return await _db.Worlds.AsNoTracking().ToListAsync();
    }

    public async Task<World?> FindByIdAsync(int id)
    {
        return await _db.Worlds.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
    }
}
