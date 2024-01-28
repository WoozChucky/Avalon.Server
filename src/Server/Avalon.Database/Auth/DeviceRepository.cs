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
        
        var id = await connection.InsertAsync(entity);

        return await FindByIdAsync(id);
    }

    public async Task<Device> UpdateAsync(Device entity)
    {
        await using var connection = new MySqlConnection(_connectionString);
        
        await connection.UpdateAsync(entity);

        return await FindByIdAsync(entity.Id);
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
}
