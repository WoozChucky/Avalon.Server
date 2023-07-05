using System.Collections.Generic;
using System.Threading.Tasks;
using Avalon.Database.Auth;
using Dapper;
using MySqlConnector;

namespace Avalon.Database.Characters;

public interface ICharacterTable
{
    Task<Character?> QueryByIdAsync(int id);
    Task<Character?> QueryByIdAndAccountAsync(int id, int account);
    Task<IEnumerable<Character>> QueryByAccountAsync(int account);
    Task<Character?> QueryByNameAsync(string name);
    Task<bool> InsertAsync(Character character);
    Task<bool> DeleteAsync(int characterId, int account);
}

public sealed class CharacterTable : ICharacterTable
{
    private const string ConnectionString =
        "Server=localhost; Port=3306; Database=characters; userid=root; Pwd=123;";
    private const string TableName = "Character";
    
    private const string CharacterSelectors = "id as Id, account as Account, name as Name, level as Level, class as Class, position_x as PositionX, position_y as PositionY";
    
    private const string GetCharacterByIdQuery = $"SELECT {CharacterSelectors} FROM `Character` WHERE id = @Id";
    private const string GetCharacterByIdAndAccountQuery = $"SELECT {CharacterSelectors} FROM `Character` WHERE id = @Id AND `account` = @Account";
    private const string GetCharactersByAccountQuery = $"SELECT {CharacterSelectors} FROM `Character` WHERE `account` = @Account";
    private const string GetCharacterByNameQuery = $"SELECT {CharacterSelectors} FROM `Character` WHERE name = @Name";
    private const string InsertCharacterQuery = "INSERT INTO `Character` (`account`, `name`, `level`, `class`, `position_x`, `position_y`) VALUES (@Account, @Name, @Level, @Class, @PositionX, @PositionY)";
    private const string DeleteCharacterQuery = "DELETE FROM `Character` WHERE id = @Id AND `account` = @Account";
    
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

    public async Task<IEnumerable<Character>> QueryByAccountAsync(int account)
    {
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        
        return await connection.QueryAsync<Character>(GetCharactersByAccountQuery, new { Account = account });
    }

    public async Task<Character?> QueryByNameAsync(string name)
    {
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        
        return await connection.QueryFirstOrDefaultAsync<Character>(GetCharacterByNameQuery, new { Name = name });
    }

    public async Task<bool> InsertAsync(Character character)
    {
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        
        var rows = await connection.ExecuteAsync(InsertCharacterQuery, new
        {
            Account = character.Account, Name = character.Name, Level = character.Level, Class = character.Class, PositionX = character.PositionX, PositionY = character.PositionY
        });
        
        return rows == 1;
    }

    public async Task<bool> DeleteAsync(int characterId, int account)
    {
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        
        var rows = await connection.ExecuteAsync(DeleteCharacterQuery, new { Id = characterId, Account = account });
        
        return rows == 1;
    }
}
