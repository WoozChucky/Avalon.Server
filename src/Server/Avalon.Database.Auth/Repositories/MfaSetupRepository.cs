using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Auth.Repositories;

public interface IMfaSetupRepository : IRepository<MFASetup, Guid>
{
    Task<MFASetup?> FindByAccountIdAsync(AccountId accountId);
}

public class MfaSetupRepository : EntityFrameworkRepository<MFASetup, Guid>, IMfaSetupRepository
{
    public MfaSetupRepository(AuthDbContext db)
        : base(db)
    {
    }


    public async Task<MFASetup?> FindByAccountIdAsync(AccountId accountId) =>
        await Context.Set<MFASetup>()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.AccountId == accountId);
}
