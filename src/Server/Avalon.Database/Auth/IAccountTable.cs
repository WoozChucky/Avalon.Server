using System.Threading.Tasks;
using Dapper;
using MySqlConnector;

namespace Avalon.Database.Auth
{
    public interface IAccountTable
    {
        Task<Account?> QueryByIdAsync(int id);
        Task<Account?> QueryByUsernameAsync(string username);

        Task<bool> InsertAccountAsync(string username, string email, byte[] totpSecret, byte[] salt, byte[] verifier, string ipAddress);
    }
    
    public sealed class AccountTable : IAccountTable
    {
        private const string ConnectionString =
            "Server=localhost; Port=3306; Database=auth; userid=root; Pwd=123;";
        private const string TableName = "Account";
        
        private const string GetAccountByUsernameQuery = "SELECT * FROM Account WHERE username = @Username";
        private const string GetAccountByIdQuery = "SELECT * FROM Account WHERE id = @Id";
        private const string InsertAccountQuery = "INSERT INTO Account (username, email, totp_secret, salt, verifier, last_ip) VALUES (@Username, @Email, @TotpSecret, @Salt, @Verifier, @IpAddress)";

        public async Task<Account?> QueryByIdAsync(int id)
        {
            await using var connection = new MySqlConnection(ConnectionString);

            return await connection.QueryFirstOrDefaultAsync<Account>(GetAccountByIdQuery, new { Id = id });
        }
        
        public async Task<Account?> QueryByUsernameAsync(string username)
        {
            await using var connection = new MySqlConnection(ConnectionString);

            return await connection.QueryFirstOrDefaultAsync<Account>(GetAccountByUsernameQuery, new { Username = username });
        }

        public async Task<bool> InsertAccountAsync(string username, string email, byte[] totpSecret, byte[] salt, byte[] verifier, string ipAddress)
        {
            await using var connection = new MySqlConnection(ConnectionString);

            var rows = await connection.ExecuteAsync(InsertAccountQuery, new
            {
                Username = username,
                Email = email,
                TotpSecret = totpSecret,
                Salt = salt, 
                Verifier = verifier,
                IpAddress = ipAddress
            });

            return rows >= 1;
        }
        
        public async Task<bool> UpdateAccountAsync(Account account)
        {
            await using var connection = new MySqlConnection(ConnectionString);

            var rows = await connection.ExecuteAsync(InsertAccountQuery, new
            {
                Username = account.Username,
                Email = account.Email,
                TotpSecret = account.TotpSecret,
                Salt = account.Salt, 
                Verifier = account.Verifier,
                IpAddress = account.LastIp
            });

            return rows >= 1;
        }
    }
}
