using Avalon.Database.Auth;
using Avalon.Database.Characters;
using Avalon.Database.World;

// docker run --detach --name avalon-mariadb -p 3306:3306 --env MARIADB_USER=admin --env MARIADB_PASSWORD=123 --env MARIADB_ROOT_PASSWORD=123  mariadb:latest

namespace Avalon.Database;

public interface IDatabaseManager
{
    IAuthDatabase Auth { get; }
        
    ICharactersDatabase Characters { get; }
        
    IWorldDatabase World { get; }
}

public class DatabaseManager : IDatabaseManager
{
    public IAuthDatabase Auth { get; }
    public ICharactersDatabase Characters { get; }
    public IWorldDatabase World { get; }
    
    public DatabaseManager()
    {
        Auth = new AuthDatabase();
        Characters = new CharactersDatabase();
        World = new WorldDatabase();
    }
}
