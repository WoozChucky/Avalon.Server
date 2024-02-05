using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Avalon.Database.Repositories;
using Avalon.Domain.Auth;
using DapperExtensions.Predicate;

namespace Avalon.Database.Auth;

public interface IRefreshTokenRepository : IRepository<RefreshToken, Guid>
{
    
}

public class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly string _connectionString;
    
    public RefreshTokenRepository(string connectionString)
    {
        _connectionString = connectionString;
    }
    
    public async Task<RefreshToken?> FindByIdAsync(Guid id)
    {
        throw new NotImplementedException();
    }

    public async Task<IEnumerable<RefreshToken>> FindAllAsync()
    {
        throw new NotImplementedException();
    }

    public async Task<IEnumerable<RefreshToken>> FindByAsync(IFieldPredicate predicate)
    {
        throw new NotImplementedException();
    }

    public async Task<IEnumerable<RefreshToken>> FindByAsync(Expression<Func<RefreshToken, bool>> predicate)
    {
        throw new NotImplementedException();
    }

    public async Task<RefreshToken> SaveAsync(RefreshToken entity)
    {
        throw new NotImplementedException();
    }

    public async Task<RefreshToken> UpdateAsync(RefreshToken entity)
    {
        throw new NotImplementedException();
    }

    public async Task<bool> DeleteAsync(RefreshToken entity)
    {
        throw new NotImplementedException();
    }
}
