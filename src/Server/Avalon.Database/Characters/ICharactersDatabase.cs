namespace Avalon.Database.Characters
{
    public interface ICharactersDatabase
    {
        ICharacterTable Character { get; }
    }
    
    public class CharactersDatabase : ICharactersDatabase
    {
        public ICharacterTable Character { get; }
        
        public CharactersDatabase()
        {
            Character = new CharacterTable();
        }
    }
}
