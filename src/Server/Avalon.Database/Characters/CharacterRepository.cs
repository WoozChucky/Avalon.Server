using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Avalon.Database.Repositories;
using Avalon.Domain.Characters;
using Dapper;
using DapperExtensions;
using DapperExtensions.Predicate;
using MySqlConnector;

namespace Avalon.Database.Characters;

public interface ICharacterRepository : IRepository<Character, int>
{
    Task<Character?> FindByNameAsync(string name);
    Task<Character?> FindByIdAndAccountAsync(int id, int accountId);
    Task<IEnumerable<Character>> FindByAccountAsync(int accountId);
}

public class CharacterRepository : ICharacterRepository
{
    private readonly string _connectionString;
    
    public CharacterRepository(string connectionString)
    {
        _connectionString = connectionString;
    }
    
    public async Task<Character?> FindByIdAsync(int id)
    {
        await using var connection = new MySqlConnection(_connectionString);
            
        return await connection.QueryFirstOrDefaultAsync<Character>("SELECT * FROM characters.`Character` WHERE Id = @Id", new { Id = id });
    }

    public async Task<IEnumerable<Character>> FindAllAsync()
    {
        await using var connection = new MySqlConnection(_connectionString);
            
        return await connection.QueryAsync<Character>("SELECT * FROM characters.`Character`");
    }

    public async Task<IEnumerable<Character>> FindByAsync(IFieldPredicate predicate)
    {
        await using var connection = new MySqlConnection(_connectionString);
            
        var characters = await connection.GetListAsync<Character>(predicate);

        return characters;
    }

    public async Task<IEnumerable<Character>> FindByAsync(Expression<Func<Character, bool>> predicate)
    {
        await using var connection = new MySqlConnection(_connectionString);
            
        var characters = await connection.QueryAsync<Character>("SELECT * FROM characters.`Character`");

        return characters.Where(predicate.Compile());
    }

    public async Task<Character> SaveAsync(Character entity)
    {
        await using var connection = new MySqlConnection(_connectionString);

        if (entity.Id != null)
        {
            var character = await FindByIdAsync(entity.Id.Value);
            
            if (character != null)
            {
                return await UpdateAsync(entity);
            }
        }
        
        var rows = await connection.ExecuteAsync("INSERT INTO characters.`Character` (`Account`, `Name`, `Level`, `Class`, `PositionX`, `PositionY`) VALUES (@Account, @Name, @Level, @Class, @PositionX, @PositionY)", new
        {
            Account = entity.Account,
            Name = entity.Name,
            Level = entity.Level,
            Class = entity.Class,
            PositionX = entity.PositionX,
            PositionY = entity.PositionY
        });
            
        if (rows == 0)
        {
            throw new Exception("Failed to save character");
        }

        return (await FindByNameAsync(entity.Name))!;
    }

    public async Task<Character> UpdateAsync(Character entity)
    {
        await using var connection = new MySqlConnection(_connectionString);
            
        var rows = await connection.ExecuteAsync("UPDATE characters.`Character` SET `Level` = @Level, `PositionX` = @PositionX, `PositionY` = @PositionY, `InstanceId` = @InstanceId, `Map` = @Map, `Online` = @Online WHERE Id = @Id AND `Account` = @Account", new
        {
            Id = entity.Id, 
            Account = entity.Account, 
            Level = entity.Level, 
            PositionX = entity.Movement.Position.X, 
            PositionY = entity.Movement.Position.Y, 
            InstanceId = entity.InstanceId, 
            Map = entity.Map, 
            Online = entity.Online
        });
            
        if (rows == 0)
        {
            throw new Exception("Failed to update character");
        }

        return (await FindByIdAsync(entity.Id!.Value))!;
    }

    public async Task<bool> DeleteAsync(Character entity)
    {
        await using var connection = new MySqlConnection(_connectionString);
            
        var rows = await connection.ExecuteAsync("DELETE FROM characters.`Character` WHERE Id = @Id AND Account = @AccountId", new { Id = entity.Id, AccountId = entity.Account });

        return rows == 1;
    }

    public async Task<Character?> FindByNameAsync(string name)
    {
        await using var connection = new MySqlConnection(_connectionString);
            
        return await connection.QueryFirstOrDefaultAsync<Character>("SELECT * FROM characters.`Character` WHERE Name = @Name", new { Name = name });
    }

    public async Task<Character?> FindByIdAndAccountAsync(int id, int accountId)
    {
        await using var connection = new MySqlConnection(_connectionString);
            
        return await connection.QueryFirstOrDefaultAsync<Character>("SELECT * FROM characters.`Character` WHERE Id = @Id AND Account = @AccountId", new { Id = id, AccountId = accountId });
    }

    public async Task<IEnumerable<Character>> FindByAccountAsync(int accountId)
    {
        await using var connection = new MySqlConnection(_connectionString);
            
        return await connection.QueryAsync<Character>("SELECT * FROM characters.`Character` WHERE Account = @AccountId", new { AccountId = accountId });
    }
}
