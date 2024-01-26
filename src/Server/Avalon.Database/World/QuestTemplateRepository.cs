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

public interface IQuestTemplateRepository : IRepository<QuestTemplate, int>
{
    
}

public class QuestTemplateRepository : IQuestTemplateRepository
{
    private readonly string _connectionString;
    
    public QuestTemplateRepository(string connectionString)
    {
        _connectionString = connectionString;
    }
    
    public async Task<QuestTemplate?> FindByIdAsync(int id)
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        return await connection.QueryFirstOrDefaultAsync<QuestTemplate>("SELECT * FROM world.`QuestTemplate` WHERE Id = @Id", new { Id = id });
    }

    public async Task<IEnumerable<QuestTemplate>> FindAllAsync()
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        return await connection.QueryAsync<QuestTemplate>("SELECT * FROM world.`QuestTemplate`");
    }

    public Task<IEnumerable<QuestTemplate>> FindByAsync(IFieldPredicate predicate)
    {
        throw new NotImplementedException();
    }

    public Task<IEnumerable<QuestTemplate>> FindByAsync(Expression<Func<QuestTemplate, bool>> predicate)
    {
        throw new NotImplementedException();
    }

    public Task<QuestTemplate> SaveAsync(QuestTemplate entity)
    {
        throw new NotImplementedException();
    }

    public Task<QuestTemplate> UpdateAsync(QuestTemplate entity)
    {
        throw new NotImplementedException();
    }

    public Task<bool> DeleteAsync(QuestTemplate entity)
    {
        throw new NotImplementedException();
    }
}
