using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Auth.Repositories;

public interface IMfaSetupRepository : IRepository<MFASetup, Guid>
{
    Task<MFASetup?> FindByAccountIdAsync(AccountId accountId, CancellationToken cancellationToken = default);
}

public class MfaSetupRepository(IDbContextFactory<AuthDbContext> contextFactory)
    : EntityFrameworkRepository<MFASetup, Guid, AuthDbContext>(contextFactory), IMfaSetupRepository
{
    public async Task<MFASetup?> FindByAccountIdAsync(AccountId accountId, CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);

        return await context.MfaSetups
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.AccountId == accountId, cancellationToken);
    }
}
