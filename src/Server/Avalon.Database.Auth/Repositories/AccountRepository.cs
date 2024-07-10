using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Auth.Database.Repositories;

public interface IAccountRepository : IRepository<Account, AccountId>
{
    Task<Account?> FindByUserNameAsync(string userName);
    Task<Account?> FindByEmailAsync(string email);
}

public class AccountRepository : EntityFrameworkRepository<Account, AccountId>, IAccountRepository
{
    public AccountRepository(AuthDbContext db)
        : base(db)
    { }
    
    public async Task<Account?> FindByUserNameAsync(string userName)
    {
        return await Context.Set<Account>()
            .AsNoTracking()
            .Where(x => x.Username == userName)
            .FirstOrDefaultAsync();
    }
    
    public async Task<Account?> FindByEmailAsync(string email)
    {
        return await Context.Set<Account>()
            .AsNoTracking()
            .Where(x => x.Email == email)
            .FirstOrDefaultAsync();
    }
}
