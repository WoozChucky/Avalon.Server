namespace Avalon.Database.World
{
    public interface IWorldDatabase
    {
        IMapTable Map { get; }
    }
    
    public class WorldDatabase : IWorldDatabase
    {
        public IMapTable Map { get; }
        
        public WorldDatabase()
        {
            Map = new MapTable();
        }
    }
}
