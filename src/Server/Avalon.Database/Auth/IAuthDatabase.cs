using Avalon.Database.Configuration;

namespace Avalon.Database.Auth
{
    public interface IAuthDatabase
    {
        IAccountTable Account { get; }
        IAccountAccessTable AccountAccess { get; }
    }

    public class AuthDatabase : IAuthDatabase
    {
        public IAccountTable Account { get; }
        public IAccountAccessTable AccountAccess { get; }
        
        public AuthDatabase(DatabaseConfiguration configuration)
        {
            var connectionString = $"Server={configuration.Auth.Host}; Port={configuration.Auth.Port}; Database={configuration.Auth.Database}; userid={configuration.Auth.Username}; Pwd={configuration.Auth.Password};";
            
            Account = new AccountTable(connectionString);
        }
    }
}
