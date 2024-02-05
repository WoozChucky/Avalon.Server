using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Avalon.Database.Repositories;
using Avalon.Domain.Auth;
using DapperExtensions.Predicate;

namespace Avalon.Database.Auth;

public interface IAvalonTokenRepository : IRepository<AvalonToken, Guid> {
    
}


public class AvalonTokenRepository : IAvalonTokenRepository
{
    private readonly string _connectionString;
    
    public AvalonTokenRepository(string connectionString)
    {
        _connectionString = connectionString;
    }
    
    public async Task<AvalonToken?> FindByIdAsync(Guid id)
    {
        throw new NotImplementedException();
    }

    public async Task<IEnumerable<AvalonToken>> FindAllAsync()
    {
        throw new NotImplementedException();
    }

    public async Task<IEnumerable<AvalonToken>> FindByAsync(IFieldPredicate predicate)
    {
        throw new NotImplementedException();
    }

    public async Task<IEnumerable<AvalonToken>> FindByAsync(Expression<Func<AvalonToken, bool>> predicate)
    {
        throw new NotImplementedException();
    }

    public async Task<AvalonToken> SaveAsync(AvalonToken entity)
    {
        throw new NotImplementedException();
    }

    public async Task<AvalonToken> UpdateAsync(AvalonToken entity)
    {
        throw new NotImplementedException();
    }

    public async Task<bool> DeleteAsync(AvalonToken entity)
    {
        throw new NotImplementedException();
    }
}
