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

namespace Avalon.Database.Auth
{
    public interface IAccountRepository : IRepository<Account, int>
    {
        Task<Account?> FindByUsernameAsync(string username);
        Task<Account?> FindByEmailAsync(string email);
    }
    
    public class AccountRepository : IAccountRepository
    {
        private readonly string _connectionString;

        public AccountRepository(string connectionString)
        {
            _connectionString = connectionString;
        }
        
        public async Task<Account?> FindByIdAsync(int id)
        {
            await using var connection = new MySqlConnection(_connectionString);
            
            return await connection.QueryFirstOrDefaultAsync<Account>("SELECT * FROM auth.Account WHERE Id = @Id", new { Id = id });
        }

        public async Task<IEnumerable<Account>> FindAllAsync()
        {
            await using var connection = new MySqlConnection(_connectionString);
            
            return await connection.QueryAsync<Account>("SELECT * FROM auth.Account");
        }

        public async Task<IEnumerable<Account>> FindByAsync(IFieldPredicate predicate)
        {
            await using var connection = new MySqlConnection(_connectionString);
            
            var accounts = await connection.GetListAsync<Account>(predicate);

            return accounts;
        }

        public async Task<IEnumerable<Account>> FindByAsync(Expression<Func<Account, bool>> predicate)
        {
            await using var connection = new MySqlConnection(_connectionString);
            
            var accounts = await connection.QueryAsync<Account>("SELECT * FROM auth.Account");

            return accounts.Where(predicate.Compile());
        }

        public async Task<Account> SaveAsync(Account entity)
        {
            await using var connection = new MySqlConnection(_connectionString);

            if (entity.Id != null)
            {
                var account = await FindByIdAsync(entity.Id.Value);
            
                if (account != null)
                {
                    return await UpdateAsync(entity);
                }
            }
            
            var rows = await connection.ExecuteAsync("INSERT INTO auth.Account (Username, Email, Salt, Verifier, LastIp, LastLogin, JoinDate) VALUES (@Username, @Email, @Salt, @Verifier, @IpAddress, @LastLogin, @JoinDate)", new
            {
                Username = entity.Username,
                Email = entity.Email,
                Salt = entity.Salt, 
                Verifier = entity.Verifier,
                IpAddress = entity.LastIp,
                LastLogin = entity.LastLogin,
                JoinDate = entity.JoinDate
            });
            
            if (rows == 0)
            {
                throw new Exception("Failed to save account");
            }

            return (await FindByUsernameAsync(entity.Username))!;
        }

        public async Task<Account> UpdateAsync(Account entity)
        {
            await using var connection = new MySqlConnection(_connectionString);
            
            var rows = await connection.ExecuteAsync("UPDATE auth.Account SET Username = @Username, Email = @Email, Salt = @Salt, Verifier = @Verifier, LastIp = @IpAddress, LastLogin = @LastLogin WHERE Id = @Id", new
            {
                Id = entity.Id,
                Username = entity.Username,
                Email = entity.Email,
                Salt = entity.Salt, 
                Verifier = entity.Verifier,
                IpAddress = entity.LastIp,
                LastLogin = entity.LastLogin
            });
            
            if (rows == 0)
            {
                throw new Exception("Failed to update account");
            }

            return (await FindByIdAsync(entity.Id!.Value))!;
        }

        public async Task<bool> DeleteAsync(Account entity)
        {
            await using var connection = new MySqlConnection(_connectionString);
            
            var rows = await connection.ExecuteAsync("DELETE FROM auth.Account WHERE Id = @Id", new { Id = entity.Id });

            return rows >= 1;
        }

        public async Task<Account?> FindByUsernameAsync(string username)
        {
            await using var connection = new MySqlConnection(_connectionString);
            
            return await connection.QueryFirstOrDefaultAsync<Account>("SELECT * FROM auth.Account WHERE Username = @Username", new { Username = username });
        }

        public async Task<Account?> FindByEmailAsync(string email)
        {
            await using var connection = new MySqlConnection(_connectionString);
            
            return await connection.QueryFirstOrDefaultAsync<Account>("SELECT * FROM auth.Account WHERE Email = @Email", new { Email = email });
        }
    }
}
