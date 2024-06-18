using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Avalon.Database.Repositories;
using Dapper;
using DapperExtensions;
using DapperExtensions.Predicate;
using MySqlConnector;

namespace Avalon.Database.Auth;

public interface IWorldRepository : IRepository<Domain.Auth.World, int>
{
    Task<Domain.Auth.World?> FindByNameAsync(string name);
}

public class WorldRepository : IWorldRepository
{
    private readonly string _connectionString;

    public WorldRepository(string connectionString)
    {
        _connectionString = connectionString;
    }
    
    public async Task<Domain.Auth.World?> FindByIdAsync(int id)
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        return await connection.QueryFirstOrDefaultAsync<Domain.Auth.World>("SELECT * FROM auth.World WHERE Id = @Id", new { Id = id });
    }

    public async Task<IEnumerable<Domain.Auth.World>> FindAllAsync()
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        return await connection.QueryAsync<Domain.Auth.World>("SELECT * FROM auth.World");
    }

    public async Task<IEnumerable<Domain.Auth.World>> FindByAsync(IFieldPredicate predicate)
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        var worlds = await connection.GetListAsync<Domain.Auth.World>(predicate);

        return worlds;
    }

    public async Task<IEnumerable<Domain.Auth.World>> FindByAsync(Expression<Func<Domain.Auth.World, bool>> predicate)
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        var worlds = await connection.QueryAsync<Domain.Auth.World>("SELECT * FROM auth.World");

        return worlds.Where(predicate.Compile());
    }

    public async Task<Domain.Auth.World> SaveAsync(Domain.Auth.World entity)
    {
        await using var connection = new MySqlConnection(_connectionString);

        if (entity.Id != null)
        {
            var world = await FindByIdAsync(entity.Id.Value);
            
            if (world != null)
            {
                return await UpdateAsync(entity);
            }
        }
        
        var rows = await connection.ExecuteAsync(
            "INSERT INTO auth.World (Name, Type, AccessLevelRequired, Host, Port, MinVersion, Version, Status, CreatedAt, UpdatedAt) " +
            "VALUES " +
            "(@Name, @Type, @AccessLevelRequired, @Host, @Port, @MinVersion, @Version, @Status, @CreatedAt, @UpdatedAt)", new
            {
                Name = entity.Name,
                Type = entity.Type,
                AccessLevelRequired = entity.AccessLevelRequired,
                Host = entity.Host,
                Port = entity.Port,
                MinVersion = entity.MinVersion,
                Version = entity.Version,
                Status = entity.Status,
                CreatedAt = entity.CreatedAt,
                UpdatedAt = entity.UpdatedAt
            });
        
        if (rows == 0)
        {
            throw new Exception("Failed to save account");
        }

        return (await FindByNameAsync(entity.Name))!;
    }

    public async Task<Domain.Auth.World> UpdateAsync(Domain.Auth.World entity)
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        var rows = await connection.ExecuteAsync(
            "UPDATE auth.World SET Name = @Name, Type = @Type, AccessLevelRequired = @AccessLevelRequired, Host = @Host, Port = @Port, MinVersion = @MinVersion, Version = @Version, Status = @Status, CreatedAt = @CreatedAt, UpdatedAt = @UpdatedAt WHERE Id = @Id", new
            {
                Id = entity.Id,
                Name = entity.Name,
                Type = entity.Type,
                AccessLevelRequired = entity.AccessLevelRequired,
                Host = entity.Host,
                Port = entity.Port,
                MinVersion = entity.MinVersion,
                Version = entity.Version,
                Status = entity.Status,
                CreatedAt = entity.CreatedAt,
                UpdatedAt = entity.UpdatedAt
            });
        
        if (rows == 0)
        {
            throw new Exception("Failed to update account");
        }

        return (await FindByIdAsync(entity.Id!.Value))!;
    }

    public async Task<bool> DeleteAsync(Domain.Auth.World entity)
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        var rows = await connection.ExecuteAsync("DELETE FROM auth.World WHERE Id = @Id", new { Id = entity.Id });
        
        return rows > 0;
    }

    public async Task<Domain.Auth.World?> FindByNameAsync(string name)
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        return await connection.QueryFirstOrDefaultAsync<Domain.Auth.World>("SELECT * FROM auth.World WHERE Name = @Name", new { Name = name });
    }
}
