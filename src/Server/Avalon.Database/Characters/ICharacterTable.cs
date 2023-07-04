using System.Threading.Tasks;
using Avalon.Database.Auth;
using Dapper;
using MySqlConnector;

namespace Avalon.Database.Characters;

public interface ICharacterTable
{
    Task<Character?> QueryByIdAsync(int id);
    Task<Character?> QueryByIdAndAccountAsync(int id, int account);
}

public sealed class CharacterTable : ICharacterTable
{
    private const string ConnectionString =
        "Server=localhost; Port=3306; Database=characters; userid=root; Pwd=123;";
    private const string TableName = "Character";
    
    private const string GetCharacterByIdQuery = "SELECT * FROM `Character` WHERE id = @Id";
    private const string GetCharacterByIdAndAccountQuery = "SELECT id as Id, account as Account, name as Name, position_x as PositionX, position_y as PositionY FROM `Character` WHERE id = @Id AND `account` = @Account";

    public async Task<Character?> QueryByIdAsync(int id)
    {
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
            
        return await connection.QueryFirstOrDefaultAsync<Character>(GetCharacterByIdQuery, new { Id = id });
    }

    public async Task<Character?> QueryByIdAndAccountAsync(int id, int account)
    {
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();

        return await connection.QueryFirstOrDefaultAsync<Character>(GetCharacterByIdAndAccountQuery, new { Id = id, Account = account });
    }
}
