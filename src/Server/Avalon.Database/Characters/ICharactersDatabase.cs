using Avalon.Configuration;

namespace Avalon.Database.Characters
{
    public interface ICharactersDatabase
    {
        ICharacterTable Character { get; }
    }
    
    public class CharactersDatabase : ICharactersDatabase
    {
        public ICharacterTable Character { get; }
        
        public CharactersDatabase(DatabaseConnection configuration)
        {
            var connectionString = $"Server={configuration.Host};" +
                                   $"Port={configuration.Port};" +
                                   $"Database={configuration.Database};" +
                                   $"userid={configuration.Username};" +
                                   $"Pwd={configuration.Password};" +
                                   $"ConvertZeroDatetime=True;" +
                                   $"AllowZeroDateTime=True";
            
            Character = new CharacterTable(connectionString);
        }
    }
}
