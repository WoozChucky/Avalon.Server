using Avalon.Database.Configuration;

namespace Avalon.Database.Characters
{
    public interface ICharactersDatabase
    {
        ICharacterTable Character { get; }
    }
    
    public class CharactersDatabase : ICharactersDatabase
    {
        public ICharacterTable Character { get; }
        
        public CharactersDatabase(DatabaseConfiguration configuration)
        {
            var connectionString = $"Server={configuration.Characters.Host}; Port={configuration.Characters.Port}; Database={configuration.Characters.Database}; userid={configuration.Characters.Username}; Pwd={configuration.Characters.Password};";
            
            Character = new CharacterTable(connectionString);
        }
    }
}
