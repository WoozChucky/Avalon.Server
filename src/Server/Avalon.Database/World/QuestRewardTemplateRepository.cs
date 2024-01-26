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


public interface IQuestRewardTemplateRepository : IRepository<QuestRewardTemplate, int>
{
    
}

public class QuestRewardTemplateRepository : IQuestRewardTemplateRepository
{
    private readonly string _connectionString;
    
    public QuestRewardTemplateRepository(string connectionString)
    {
        _connectionString = connectionString;
    }
    
    public async Task<QuestRewardTemplate?> FindByIdAsync(int id)
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        return await connection.QueryFirstOrDefaultAsync<QuestRewardTemplate>("SELECT * FROM world.`QuestRewardTemplate` WHERE Id = @Id", new { Id = id });
    }

    public async Task<IEnumerable<QuestRewardTemplate>> FindAllAsync()
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        return await connection.QueryAsync<QuestRewardTemplate>("SELECT * FROM world.`QuestRewardTemplate`");
    }

    public Task<IEnumerable<QuestRewardTemplate>> FindByAsync(IFieldPredicate predicate)
    {
        throw new NotImplementedException();
    }

    public Task<IEnumerable<QuestRewardTemplate>> FindByAsync(Expression<Func<QuestRewardTemplate, bool>> predicate)
    {
        throw new NotImplementedException();
    }

    public Task<QuestRewardTemplate> SaveAsync(QuestRewardTemplate entity)
    {
        throw new NotImplementedException();
    }

    public Task<QuestRewardTemplate> UpdateAsync(QuestRewardTemplate entity)
    {
        throw new NotImplementedException();
    }

    public Task<bool> DeleteAsync(QuestRewardTemplate entity)
    {
        throw new NotImplementedException();
    }
}
