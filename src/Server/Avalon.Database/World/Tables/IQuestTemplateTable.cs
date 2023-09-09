using System.Collections.Generic;
using System.Threading.Tasks;
using Avalon.Database.World.Model;
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
    
    private const string ConnectionString =
        "Server=localhost; Port=3306; Database=world; userid=root; Pwd=123;";
    private const string TableName = "QuestTemplate";
    
    private const string GetAllQuery = $"SELECT * FROM `QuestTemplate`";
    private const string GetByIdQuery = $"SELECT * FROM `QuestTemplate` WHERE id = @Id";
    
    public async Task<IEnumerable<QuestTemplate>> QueryAllAsync()
    {
        await using var connection = new MySqlConnection(ConnectionString);
        
        return await connection.QueryAsync<QuestTemplate>(GetAllQuery);
    }
    
    public async Task<QuestTemplate?> QueryByIdAsync(int id)
    {
        await using var connection = new MySqlConnection(ConnectionString);
        
        return await connection.QueryFirstOrDefaultAsync<QuestTemplate>(GetByIdQuery, new { Id = id });
    }
}
