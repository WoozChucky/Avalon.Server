using System.Collections.Generic;
using System.Threading.Tasks;
using Avalon.Database.World.Model;
using Dapper;
using MySqlConnector;

namespace Avalon.Database.World.Tables;

public interface IMapTable
{
    Task<IEnumerable<Map>> QueryAllAsync();
}

public class MapTable : IMapTable
{
    private readonly string _connectionString;
    
    private const string TableName = "Map";
    
    private const string GetAllQuery = $"SELECT * FROM `Map`";
    private const string GetByIdQuery = $"SELECT * FROM `Map` WHERE id = @Id";
    
    public MapTable(string connectionString)
    {
        _connectionString = connectionString;
    }
    
    public async Task<IEnumerable<Map>> QueryAllAsync()
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        return await connection.QueryAsync<Map>(GetAllQuery);
    }
}
