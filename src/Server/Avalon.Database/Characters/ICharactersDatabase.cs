namespace Avalon.Database.Characters
{
    public interface ICharactersDatabase
    {
        ICharacterRepository Character { get; }
    }
    
    public class CharactersDatabase : ICharactersDatabase
    {
        public ICharacterRepository Character { get; }
        
        public CharactersDatabase(ICharacterRepository characterRepository)
        {
            Character = characterRepository;
        }
    }
}
