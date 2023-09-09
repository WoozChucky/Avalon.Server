using System.Collections.Generic;
using System.Threading.Tasks;
using Avalon.Database.World.Model;
using Dapper;
using MySqlConnector;

namespace Avalon.Database.World.Tables;

public interface IQuestRewardTable
{
    Task<IEnumerable<QuestReward>> QueryAllAsync();
    Task<QuestReward?> QueryByQuestIdAsync(int id);
    Task<QuestReward?> QueryByRewardIdAsync(int id);
}

public class QuestRewardTable : IQuestRewardTable
{
    
    private const string ConnectionString =
        "Server=localhost; Port=3306; Database=world; userid=root; Pwd=123;";
    private const string TableName = "QuestReward";
    
    private const string GetAllQuery = $"SELECT * FROM `QuestReward`";
    private const string GetByQuestIdQuery = $"SELECT * FROM `QuestReward` WHERE QuestId = @Id";
    private const string GetByRewardIdQuery = $"SELECT * FROM `QuestReward` WHERE RewardId = @Id";
    
    public async Task<IEnumerable<QuestReward>> QueryAllAsync()
    {
        await using var connection = new MySqlConnection(ConnectionString);
        
        return await connection.QueryAsync<QuestReward>(GetAllQuery);
    }

    public async Task<QuestReward?> QueryByQuestIdAsync(int id)
    {
        await using var connection = new MySqlConnection(ConnectionString);
        
        return await connection.QueryFirstOrDefaultAsync<QuestReward>(GetByQuestIdQuery, new { Id = id });
    }

    public async Task<QuestReward?> QueryByRewardIdAsync(int id)
    {
        await using var connection = new MySqlConnection(ConnectionString);
        
        return await connection.QueryFirstOrDefaultAsync<QuestReward>(GetByRewardIdQuery, new { Id = id });
    }
}
