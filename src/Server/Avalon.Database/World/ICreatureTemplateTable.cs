using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using MySqlConnector;

namespace Avalon.Database.World;

public interface ICreatureTemplateTable
{
    Task<IEnumerable<CreatureTemplate>> QueryAllAsync();
    Task<CreatureTemplate?> QueryByIdAsync(int id);
}

public class CreatureTemplateTable : ICreatureTemplateTable
{
    
    private const string ConnectionString =
        "Server=localhost; Port=3306; Database=world; userid=root; Pwd=123;";
    private const string TableName = "CreatureTemplate";
    
    private const string GetAllQuery = $"SELECT * FROM `CreatureTemplate`";
    private const string GetByIdQuery = $"SELECT * FROM `CreatureTemplate` WHERE id = @Id";
    
    public async Task<IEnumerable<CreatureTemplate>> QueryAllAsync()
    {
        await using var connection = new MySqlConnection(ConnectionString);
        
        return await connection.QueryAsync<CreatureTemplate>(GetAllQuery);
    }
    
    public async Task<CreatureTemplate?> QueryByIdAsync(int id)
    {
        await using var connection = new MySqlConnection(ConnectionString);
        
        return await connection.QueryFirstOrDefaultAsync<CreatureTemplate>(GetByIdQuery, new { Id = id });
    }
}
