using System.Collections.Generic;
using System.Threading.Tasks;
using Avalon.Database.World.Model;
using Dapper;
using MySqlConnector;

namespace Avalon.Database.World.Tables;

public interface IQuestRewardTemplateTable
{
    Task<IEnumerable<QuestRewardTemplate>> QueryAllAsync();
    Task<QuestRewardTemplate?> QueryByIdAsync(int id);
}

public class QuestRewardTemplateTable : IQuestRewardTemplateTable
{
    
    private const string ConnectionString =
        "Server=localhost; Port=3306; Database=world; userid=root; Pwd=123;";
    private const string TableName = "QuestRewardTemplate";
    
    private const string GetAllQuery = $"SELECT * FROM `QuestRewardTemplate`";
    private const string GetByIdQuery = $"SELECT * FROM `QuestRewardTemplate` WHERE id = @Id";
    
    public async Task<IEnumerable<QuestRewardTemplate>> QueryAllAsync()
    {
        await using var connection = new MySqlConnection(ConnectionString);
        
        return await connection.QueryAsync<QuestRewardTemplate>(GetAllQuery);
    }

    public async Task<QuestRewardTemplate?> QueryByIdAsync(int id)
    {
        await using var connection = new MySqlConnection(ConnectionString);
        
        return await connection.QueryFirstOrDefaultAsync<QuestRewardTemplate>(GetByIdQuery, new { Id = id });
    }
}
