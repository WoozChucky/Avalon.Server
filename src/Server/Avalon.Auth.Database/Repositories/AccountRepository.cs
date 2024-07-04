using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Auth.Database.Repositories;

public interface IAccountRepository
{
    Task<Account?> FindByIdAsync(int id);
    Task<Account?> FindByUserNameAsync(string userName);
    Task<Account?> FindByEmailAsync(string email);
    Task<Account> CreateAsync(Account account);
    Task<Account> UpdateAsync(Account account);
}

public class AccountRepository : IAccountRepository
{
    private readonly AuthDbContext _db;
    
    public AccountRepository(AuthDbContext db)
    {
        _db = db;
    }
    
    public async Task<Account?> FindByUserNameAsync(string userName)
    {
        return await _db.Accounts
            .AsNoTracking()
            .Where(x => x.Username == userName)
            .FirstOrDefaultAsync();
    }
    
    public async Task<Account?> FindByIdAsync(int id)
    {
        return await _db.Accounts
            .AsNoTracking()
            .Where(x => x.Id == id)
            .FirstOrDefaultAsync();
    }
    
    public async Task<Account?> FindByEmailAsync(string email)
    {
        return await _db.Accounts
            .AsNoTracking()
            .Where(x => x.Email == email)
            .FirstOrDefaultAsync();
    }
    
    public async Task<Account> CreateAsync(Account account)
    {
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync();
        
        return account;
    }
    
    public async Task<Account> UpdateAsync(Account account)
    {
        _db.Accounts.Update(account);
        var rows = await _db.SaveChangesAsync();
        
        if (rows == 0)
        {
            throw new InvalidOperationException("Failed to update account");
        }
        
        return account;
    }
}
