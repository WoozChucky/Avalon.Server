using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Avalon.Database.Repositories;
using Avalon.Domain.World;
using Dapper;
using DapperExtensions.Predicate;
using MySqlConnector;

namespace Avalon.Database.World;

public interface IMapRepository : IRepository<Map, int>
{
    
}

public class MapRepository : IMapRepository
{
    private readonly string _connectionString;
    
    public MapRepository(string connectionString)
    {
        _connectionString = connectionString;
    }
    
    public Task<Map?> FindByIdAsync(int id)
    {
        throw new NotImplementedException();
    }

    public async Task<IEnumerable<Map>> FindAllAsync()
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        return await connection.QueryAsync<Map>("SELECT * FROM world.`Map`");
    }

    public Task<IEnumerable<Map>> FindByAsync(IFieldPredicate predicate)
    {
        throw new NotImplementedException();
    }

    public Task<IEnumerable<Map>> FindByAsync(Expression<Func<Map, bool>> predicate)
    {
        throw new NotImplementedException();
    }

    public Task<Map> SaveAsync(Map entity)
    {
        throw new NotImplementedException();
    }

    public Task<Map> UpdateAsync(Map entity)
    {
        throw new NotImplementedException();
    }

    public Task<bool> DeleteAsync(Map entity)
    {
        throw new NotImplementedException();
    }
}
