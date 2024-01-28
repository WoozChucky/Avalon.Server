using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Avalon.Database.Repositories;
using Avalon.Domain.Auth;
using Dapper;
using DapperExtensions;
using DapperExtensions.Predicate;
using MySqlConnector;

namespace Avalon.Database.Auth;

public interface IDeviceRepository : IRepository<Device, int>
{
    Task<IEnumerable<Device>> FindByAccountIdAsync(int accountId);
    Task<Device?> FindByNameAndAccountIdAsync(string name, int accountId);
}

public class DeviceRepository : IDeviceRepository
{
    private readonly string _connectionString;
    
    public DeviceRepository(string connectionString)
    {
        _connectionString = connectionString;
    }
    
    public async Task<Device?> FindByIdAsync(int id)
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        return await connection.QueryFirstOrDefaultAsync<Device>("SELECT * FROM auth.Device WHERE Id = @Id", new { Id = id });
    }

    public async Task<IEnumerable<Device>> FindAllAsync()
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        return await connection.QueryAsync<Device>("SELECT * FROM auth.Device");
    }

    public async Task<IEnumerable<Device>> FindByAsync(IFieldPredicate predicate)
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        var accounts = await connection.GetListAsync<Device>(predicate);

        return accounts;
    }

    public async Task<IEnumerable<Device>> FindByAsync(Expression<Func<Device, bool>> predicate)
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        var accounts = await connection.QueryAsync<Device>("SELECT * FROM auth.Device");

        return accounts.Where(predicate.Compile());
    }

    public async Task<Device> SaveAsync(Device entity)
    {
        await using var connection = new MySqlConnection(_connectionString);

        if (entity.Id != null)
        {
            var device = await FindByIdAsync(entity.Id.Value);
            
            if (device != null)
            {
                return await UpdateAsync(entity);
            }
        }
            
        var rows = await connection.ExecuteAsync("INSERT INTO auth.Device (AccountId, Name, Metadata, Trusted, TrustEnd, LastUsage) VALUES (@AccountId, @Name, @Metadata, @Trusted, @TrustEnd, @LastUsage)", new
        {
            AccountId = entity.AccountId,
            Name = entity.Name,
            Metadata = entity.Metadata,
            Trusted = entity.Trusted,
            TrustEnd = entity.TrustEnd,
            LastUsage = entity.LastUsage
        });
            
        if (rows == 0)
        {
            throw new Exception("Failed to save device");
        }

        return (await FindByNameAndAccountIdAsync(entity.Name, entity.AccountId))!;
    }

    public async Task<Device> UpdateAsync(Device entity)
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        var rows = await connection.ExecuteAsync("UPDATE auth.Device SET (AccountId = @AccountId, Name = @Name, Metadata = @Metadata, Trusted = @Trusted, TrustEnd = @TrustEnd, LastUsage = @LastUsage) WHERE Id = @Id", new
        {
            Id = entity.Id,
            AccountId = entity.AccountId,
            Name = entity.Name,
            Metadata = entity.Metadata,
            Trusted = entity.Trusted,
            TrustEnd = entity.TrustEnd,
            LastUsage = entity.LastUsage
        });
            
        if (rows != 1)
        {
            throw new Exception("Failed to update device");
        }

        return (await FindByIdAsync(entity.Id!.Value))!;
    }

    public async Task<bool> DeleteAsync(Device entity)
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        return await connection.DeleteAsync(entity);
    }

    public async Task<IEnumerable<Device>> FindByAccountIdAsync(int accountId)
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        return await connection.QueryAsync<Device>("SELECT * FROM auth.Device WHERE AccountId = @AccountId", new { AccountId = accountId });
    }

    public async Task<Device?> FindByNameAndAccountIdAsync(string name, int accountId)
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        return await connection.QueryFirstOrDefaultAsync<Device>("SELECT * FROM auth.Device WHERE Name = @Name AND AccountId = @AccountId", new { Name = name, AccountId = accountId });
    }
}
