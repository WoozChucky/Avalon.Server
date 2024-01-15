using System.Collections.Generic;
using System.Threading.Tasks;
using Avalon.Domain.World;
using Dapper;
using MySqlConnector;

namespace Avalon.Database.World.Tables;

public interface IQuestTemplateTable
{
    Task<IEnumerable<QuestTemplate>> QueryAllAsync();
    Task<QuestTemplate?> QueryByIdAsync(int id);
}

public class QuestTemplateTable : IQuestTemplateTable
{
    private readonly string _connectionString;

    public QuestTemplateTable(string connectionString)
    {
        _connectionString = connectionString;
    }

    private const string TableName = "QuestTemplate";
    
    private const string GetAllQuery = $"SELECT * FROM `QuestTemplate`";
    private const string GetByIdQuery = $"SELECT * FROM `QuestTemplate` WHERE id = @Id";
    
    public async Task<IEnumerable<QuestTemplate>> QueryAllAsync()
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        return await connection.QueryAsync<QuestTemplate>(GetAllQuery);
    }
    
    public async Task<QuestTemplate?> QueryByIdAsync(int id)
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        return await connection.QueryFirstOrDefaultAsync<QuestTemplate>(GetByIdQuery, new { Id = id });
    }
}
