using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Avalon.Database.Repositories;
using Avalon.Domain.World;
using Dapper;
using DapperExtensions;
using DapperExtensions.Predicate;
using MySqlConnector;

namespace Avalon.Database.World;

public interface IQuestRewardRepository : IRepository<QuestReward, int>
{
    Task<QuestReward?> FindByQuestIdAsync(int questId);
    Task<QuestReward?> FindByRewardIdAsync(int rewardId);
}

public class QuestRewardRepository : IQuestRewardRepository
{
    private readonly string _connectionString;
    
    public QuestRewardRepository(string connectionString)
    {
        _connectionString = connectionString;
    }
    
    public async Task<QuestReward?> FindByIdAsync(int id)
    {
        throw new InvalidOperationException();
    }

    public async Task<IEnumerable<QuestReward>> FindAllAsync()
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        return await connection.QueryAsync<QuestReward>("SELECT * FROM world.`QuestReward`");
    }

    public async Task<IEnumerable<QuestReward>> FindByAsync(IFieldPredicate predicate)
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        var questRewards = await connection.GetListAsync<QuestReward>(predicate);
        
        return questRewards;
    }

    public async Task<IEnumerable<QuestReward>> FindByAsync(Expression<Func<QuestReward, bool>> predicate)
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        var questRewards = await connection.QueryAsync<QuestReward>("SELECT * FROM world.`QuestReward`");
        
        return questRewards.Where(predicate.Compile());
    }

    public Task<QuestReward> SaveAsync(QuestReward entity)
    {
        throw new NotImplementedException();
    }

    public Task<QuestReward> UpdateAsync(QuestReward entity)
    {
        throw new NotImplementedException();
    }

    public Task<bool> DeleteAsync(QuestReward entity)
    {
        throw new NotImplementedException();
    }

    public async Task<QuestReward?> FindByQuestIdAsync(int questId)
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        return await connection.QueryFirstOrDefaultAsync<QuestReward>("SELECT * FROM world.`QuestReward` WHERE QuestId = @Id", new { Id = questId });
    }

    public async Task<QuestReward?> FindByRewardIdAsync(int rewardId)
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        return await connection.QueryFirstOrDefaultAsync<QuestReward>("SELECT * FROM world.`QuestReward` WHERE RewardId = @Id", new { Id = rewardId });
    }
}
