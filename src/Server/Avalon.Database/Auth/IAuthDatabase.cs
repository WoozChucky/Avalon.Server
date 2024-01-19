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
        
        public AuthDatabase(DatabaseConfiguration configuration)
        {
            var connectionString = $"Server={configuration.Auth.Host};" +
                                   $"Port={configuration.Auth.Port};" +
                                   $"Database={configuration.Auth.Database};" +
                                   $"userid={configuration.Auth.Username};" +
                                   $"Pwd={configuration.Auth.Password};" +
                                   $"ConvertZeroDatetime=True;" +
                                   $"AllowZeroDateTime=True";
            
            Account = new AccountTable(connectionString);
        }
    }
}
