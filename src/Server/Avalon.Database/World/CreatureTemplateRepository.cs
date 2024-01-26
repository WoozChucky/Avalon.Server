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

public interface ICreatureTemplateRepository : IRepository<CreatureTemplate, int>
{
    
}

public class CreatureTemplateRepository : ICreatureTemplateRepository
{
    private readonly string _connectionString;
    
    public CreatureTemplateRepository(string connectionString)
    {
        _connectionString = connectionString;
    }
    
    public async Task<CreatureTemplate?> FindByIdAsync(int id)
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        return await connection.QueryFirstOrDefaultAsync<CreatureTemplate>("SELECT * FROM world.`CreatureTemplate` WHERE Id = @Id", new { Id = id });
    }

    public async Task<IEnumerable<CreatureTemplate>> FindAllAsync()
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        return await connection.QueryAsync<CreatureTemplate>("SELECT * FROM world.`CreatureTemplate`");
    }

    public Task<IEnumerable<CreatureTemplate>> FindByAsync(IFieldPredicate predicate)
    {
        throw new NotImplementedException();
    }

    public Task<IEnumerable<CreatureTemplate>> FindByAsync(Expression<Func<CreatureTemplate, bool>> predicate)
    {
        throw new NotImplementedException();
    }

    public Task<CreatureTemplate> SaveAsync(CreatureTemplate entity)
    {
        throw new NotImplementedException();
    }

    public Task<CreatureTemplate> UpdateAsync(CreatureTemplate entity)
    {
        throw new NotImplementedException();
    }

    public Task<bool> DeleteAsync(CreatureTemplate entity)
    {
        throw new NotImplementedException();
    }
}
