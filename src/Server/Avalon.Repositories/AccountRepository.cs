using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalon.Database.Auth;
using Dapper;
using MySqlConnector;

namespace Avalon.Repositories
{
    public interface IAccountRepository : IRepository<Account, int>
    {
        Task<Account?> FindByUsernameAsync(string username);
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
            
            return await connection.QueryFirstOrDefaultAsync<Account>("SELECT * FROM auth.Account", new { Id = id });
        }

        public async Task<IEnumerable<Account>> FindAllAsync()
        {
            await using var connection = new MySqlConnection(_connectionString);
            
            return await connection.QueryAsync<Account>("SELECT * FROM auth.Account");
        }

        public async Task<IEnumerable<Account>> FindByAsync(Predicate<Account> predicate)
        {
            await using var connection = new MySqlConnection(_connectionString);
            
            var accounts = await connection.QueryAsync<Account>("SELECT * FROM auth.Account");

            return accounts.Where(account => predicate(account)).ToList();
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
            
            var rows = await connection.ExecuteAsync("INSERT INTO auth.Account (username, email, totp_secret, salt, verifier, last_ip) VALUES (@Username, @Email, @TotpSecret, @Salt, @Verifier, @IpAddress)", new
            {
                Username = entity.Username,
                Email = entity.Email,
                TotpSecret = entity.TotpSecret,
                Salt = entity.Salt, 
                Verifier = entity.Verifier,
                IpAddress = entity.LastIp
            });
            
            if (rows == 0)
            {
                throw new Exception("Failed to insert account");
            }

            return (await FindByUsernameAsync(entity.Username))!;
        }

        public async Task<Account> UpdateAsync(Account entity)
        {
            await using var connection = new MySqlConnection(_connectionString);
            
            var rows = await connection.ExecuteAsync("UPDATE auth.Account SET username = @Username, email = @Email, totp_secret = @TotpSecret, salt = @Salt, verifier = @Verifier, last_ip = @IpAddress WHERE id = @Id", new
            {
                Id = entity.Id,
                Username = entity.Username,
                Email = entity.Email,
                TotpSecret = entity.TotpSecret,
                Salt = entity.Salt, 
                Verifier = entity.Verifier,
                IpAddress = entity.LastIp,
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
            
            var rows = await connection.ExecuteAsync("DELETE FROM auth.Account WHERE id = @Id", new { Id = entity.Id });

            return rows >= 1;
        }

        public async Task<Account?> FindByUsernameAsync(string username)
        {
            await using var connection = new MySqlConnection(_connectionString);
            
            return await connection.QueryFirstOrDefaultAsync<Account>("SELECT * FROM auth.Account WHERE username = @Username", new { Username = username });
        }
    }
}
