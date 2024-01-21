using Avalon.Configuration;

namespace Avalon.Database.Auth
{
    public interface IAuthDatabase
    {
        IAccountTable Account { get; }
    }

    public class AuthDatabase : IAuthDatabase
    {
        public IAccountTable Account { get; }
        
        public AuthDatabase(DatabaseConnection configuration)
        {
            var connectionString = $"Server={configuration.Host};" +
                                   $"Port={configuration.Port};" +
                                   $"Database={configuration.Database};" +
                                   $"userid={configuration.Username};" +
                                   $"Pwd={configuration.Password};" +
                                   $"ConvertZeroDatetime=True;" +
                                   $"AllowZeroDateTime=True";
            
            Account = new AccountTable(connectionString);
        }
    }
}
