using System.Threading.Tasks;
using Dapper;
using MySqlConnector;

namespace Avalon.Database.Auth
{
    public interface IAccountTable
    {
        Task<Account?> QueryByIdAsync(int id);
        Task<Account?> QueryByUsernameAsync(string username);
    }
    
    public sealed class AccountTable : IAccountTable
    {
        private const string ConnectionString =
            "Server=localhost; Port=3306; Database=auth; userid=root; Pwd=123;";
        private const string TableName = "Account";
        
        private const string GetAccountByUsernameQuery = "SELECT * FROM Account WHERE username = @Username";
        private const string GetAccountByIdQuery = "SELECT * FROM Account WHERE id = @Id";

        public async Task<Account?> QueryByIdAsync(int id)
        {
            await using var connection = new MySqlConnection(ConnectionString);
            await connection.OpenAsync();
            
            return await connection.QueryFirstOrDefaultAsync<Account>(GetAccountByIdQuery, new { Id = id });
        }
        
        public async Task<Account?> QueryByUsernameAsync(string username)
        {
            await using var connection = new MySqlConnection(ConnectionString);
            await connection.OpenAsync();
            
            return await connection.QueryFirstOrDefaultAsync<Account>(GetAccountByUsernameQuery, new { Username = username });
        }
        
        
    }
}
