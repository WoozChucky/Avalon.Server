using Avalon.Database;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Auth.Database.Repositories;

public interface IMfaSetupRepository : IRepository<MFASetup, Guid>
{
    Task<MFASetup?> FindByAccountIdAsync(ulong accountId);
}

public class MfaSetupRepository : EntityFrameworkRepository<MFASetup, Guid>, IMfaSetupRepository
{
    public MfaSetupRepository(AuthDbContext db)
        : base(db)
    { }


    public async Task<MFASetup?> FindByAccountIdAsync(ulong accountId)
    {
        return await Context.Set<MFASetup>()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.AccountId == accountId);
    }
}
