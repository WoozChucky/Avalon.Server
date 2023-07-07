using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using MySqlConnector;

namespace Avalon.Database.World;

public interface IMapTable
{
    Task<IEnumerable<Map>> QueryAllAsync();
}

public class MapTable : IMapTable
{
    
    private const string ConnectionString =
        "Server=localhost; Port=3306; Database=world; userid=root; Pwd=123;";
    private const string TableName = "Map";
    
    private const string GetAllQuery = $"SELECT * FROM `Map`";
    private const string GetByIdQuery = $"SELECT * FROM `Map` WHERE id = @Id";
    
    public async Task<IEnumerable<Map>> QueryAllAsync()
    {
        await using var connection = new MySqlConnection(ConnectionString);
        
        return await connection.QueryAsync<Map>(GetAllQuery);
    }
}
