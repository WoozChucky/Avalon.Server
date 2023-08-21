namespace Avalon.Database.World
{
    public interface IWorldDatabase
    {
        IMapTable Map { get; }
        ICreatureTemplateTable CreatureTemplate { get; }
        IQuestTemplateTable QuestTemplate { get; }
    }
    
    public class WorldDatabase : IWorldDatabase
    {
        public IMapTable Map { get; }
        public ICreatureTemplateTable CreatureTemplate { get; }
        public IQuestTemplateTable QuestTemplate { get; }

        public WorldDatabase()
        {
            Map = new MapTable();
            CreatureTemplate = new CreatureTemplateTable();
            QuestTemplate = new QuestTemplateTable();
        }
    }
}
