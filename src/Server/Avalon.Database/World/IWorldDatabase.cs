using Avalon.Database.World.Tables;

namespace Avalon.Database.World
{
    public interface IWorldDatabase
    {
        IMapTable Map { get; }
        ICreatureTemplateTable CreatureTemplate { get; }
        IQuestTemplateTable QuestTemplate { get; }
        IQuestRewardTable QuestReward { get; }
        IQuestRewardTemplateTable QuestRewardTemplate { get; }
    }
    
    public class WorldDatabase : IWorldDatabase
    {
        public IMapTable Map { get; }
        public ICreatureTemplateTable CreatureTemplate { get; }
        public IQuestTemplateTable QuestTemplate { get; }
        public IQuestRewardTable QuestReward { get; }
        public IQuestRewardTemplateTable QuestRewardTemplate { get; }

        public WorldDatabase()
        {
            Map = new MapTable();
            CreatureTemplate = new CreatureTemplateTable();
            QuestTemplate = new QuestTemplateTable();
            QuestReward = new QuestRewardTable();
            QuestRewardTemplate = new QuestRewardTemplateTable();
        }
    }
}
