using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Auth.Repositories;

public interface IAccountRepository : IRepository<Account, AccountId>
{
    Task<Account?> FindByUserNameAsync(string userName, CancellationToken cancellationToken = default);
    Task<Account?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);
}

public class AccountRepository(IDbContextFactory<AuthDbContext> contextFactory)
    : EntityFrameworkRepository<Account, AccountId, AuthDbContext>(contextFactory), IAccountRepository
{
    public async Task<Account?> FindByUserNameAsync(string userName, CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);

        return await context.Accounts
            .AsNoTracking()
            .Where(x => x.Username == userName)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<Account?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);

        return await context.Accounts
            .AsNoTracking()
            .Where(x => x.Email == email)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
