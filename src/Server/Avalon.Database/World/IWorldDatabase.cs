namespace Avalon.Database.World
{
    public interface IWorldDatabase
    {
        IMapTable Map { get; }
        ICreatureTemplateTable CreatureTemplate { get; }
    }
    
    public class WorldDatabase : IWorldDatabase
    {
        public IMapTable Map { get; }
        public ICreatureTemplateTable CreatureTemplate { get; }

        public WorldDatabase()
        {
            Map = new MapTable();
            CreatureTemplate = new CreatureTemplateTable();
        }
    }
}
