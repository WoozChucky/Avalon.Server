using System.Collections.Generic;
using System.Threading.Tasks;
using Avalon.Domain.World;
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
    private readonly string _connectionString;

    public QuestRewardTemplateTable(string connectionString)
    {
        _connectionString = connectionString;
    }

    private const string TableName = "QuestRewardTemplate";
    
    private const string GetAllQuery = $"SELECT * FROM `QuestRewardTemplate`";
    private const string GetByIdQuery = $"SELECT * FROM `QuestRewardTemplate` WHERE id = @Id";
    
    public async Task<IEnumerable<QuestRewardTemplate>> QueryAllAsync()
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        return await connection.QueryAsync<QuestRewardTemplate>(GetAllQuery);
    }

    public async Task<QuestRewardTemplate?> QueryByIdAsync(int id)
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        return await connection.QueryFirstOrDefaultAsync<QuestRewardTemplate>(GetByIdQuery, new { Id = id });
    }
}
