using System.Collections.Generic;
using System.Threading.Tasks;
using Avalon.Database.World.Model;
using Dapper;
using MySqlConnector;

namespace Avalon.Database.World.Tables;

public interface ICreatureTemplateTable
{
    Task<IEnumerable<CreatureTemplate>> QueryAllAsync();
    Task<CreatureTemplate?> QueryByIdAsync(int id);
}

public class CreatureTemplateTable : ICreatureTemplateTable
{
    private readonly string _connectionString;

    public CreatureTemplateTable(string connectionString)
    {
        _connectionString = connectionString;
    }

    private const string TableName = "CreatureTemplate";
    
    private const string GetAllQuery = $"SELECT * FROM `CreatureTemplate`";
    private const string GetByIdQuery = $"SELECT * FROM `CreatureTemplate` WHERE id = @Id";
    
    public async Task<IEnumerable<CreatureTemplate>> QueryAllAsync()
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        return await connection.QueryAsync<CreatureTemplate>(GetAllQuery);
    }
    
    public async Task<CreatureTemplate?> QueryByIdAsync(int id)
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        return await connection.QueryFirstOrDefaultAsync<CreatureTemplate>(GetByIdQuery, new { Id = id });
    }
}
