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
    public interface IMFASetupRepository : IRepository<MFASetup, Guid>
    {
        Task<MFASetup?> FindByAccountIdAsync(int accountId);
    }
    
    public class MFASetupRepository : IMFASetupRepository
    {
        private readonly string _connectionString;

        public MFASetupRepository(string connectionString)
        {
            _connectionString = connectionString;
        }
        
        public async Task<MFASetup?> FindByIdAsync(Guid id)
        {
            await using var connection = new MySqlConnection(_connectionString);
            
            return await connection.QueryFirstOrDefaultAsync<MFASetup>("SELECT * FROM auth.MFASetup WHERE Id = @Id", new { Id = id });
        }

        public async Task<IEnumerable<MFASetup>> FindAllAsync()
        {
            await using var connection = new MySqlConnection(_connectionString);
            
            return await connection.QueryAsync<MFASetup>("SELECT * FROM auth.MFASetup");
        }

        public async Task<IEnumerable<MFASetup>> FindByAsync(IFieldPredicate predicate)
        {
            await using var connection = new MySqlConnection(_connectionString);
            
            var accounts = await connection.GetListAsync<MFASetup>(predicate);

            return accounts;
        }

        public async Task<IEnumerable<MFASetup>> FindByAsync(Expression<Func<MFASetup, bool>> predicate)
        {
            await using var connection = new MySqlConnection(_connectionString);
            
            var accounts = await connection.QueryAsync<MFASetup>("SELECT * FROM auth.MFASetup");

            return accounts.Where(predicate.Compile());
        }

        public async Task<MFASetup> SaveAsync(MFASetup entity)
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
            
            var rows = await connection.ExecuteAsync("INSERT INTO auth.MFASetup " +
                                                     "(AccountId, Secret, RecoveryCode1, RecoveryCode2, RecoveryCode3, Status, CreatedAt)" +
                                                     " VALUES (@AccountId, @Secret, @RecoveryCode1, @RecoveryCode2, @RecoveryCode3, @Status, @CreatedAt)", new
            {
                AccountId = entity.AccountId,
                Secret = entity.Secret,
                RecoveryCode1 = entity.RecoveryCode1,
                RecoveryCode2 = entity.RecoveryCode2, 
                RecoveryCode3 = entity.RecoveryCode3,
                Status = entity.Status,
                CreatedAt = DateTime.UtcNow
            });
            
            if (rows == 0)
            {
                throw new Exception("Failed to save mfa setup");
            }

            return (await FindByAccountIdAsync(entity.AccountId))!;
        }

        public async Task<MFASetup> UpdateAsync(MFASetup entity)
        {
            await using var connection = new MySqlConnection(_connectionString);
            
            var rows = await connection.ExecuteAsync("UPDATE auth.MFASetup SET Secret = @Secret, RecoveryCode1 = @RecoveryCode1, RecoveryCode2 = @RecoveryCode2, RecoveryCode3 = @RecoveryCode3, Status = @Status, ConfirmedAt = @ConfirmedAt WHERE Id = @Id", new
            {
                entity.Secret,
                entity.RecoveryCode1,
                entity.RecoveryCode2,
                entity.RecoveryCode3,
                entity.Status,
                entity.ConfirmedAt,
                entity.Id
            });
            
            if (rows == 0)
            {
                throw new Exception("Failed to update mfa setup");
            }

            return (await FindByIdAsync(entity.Id!.Value))!;
        }

        public async Task<bool> DeleteAsync(MFASetup entity)
        {
            await using var connection = new MySqlConnection(_connectionString);
            
            var rows = await connection.ExecuteAsync("DELETE FROM auth.MFASetup WHERE Id = @Id", new { Id = entity.Id });

            return rows >= 1;
        }

        public async Task<MFASetup?> FindByAccountIdAsync(int accountId)
        {
            await using var connection = new MySqlConnection(_connectionString);
            
            return await connection.QueryFirstOrDefaultAsync<MFASetup>("SELECT * FROM auth.MFASetup WHERE AccountId = @AccountId", new { AccountId = accountId });
        }
    }
}
