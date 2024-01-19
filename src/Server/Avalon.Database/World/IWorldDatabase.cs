using Avalon.Configuration;
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
        
        public WorldDatabase(DatabaseConfiguration configuration)
        {
            var connectionString = $"Server={configuration.World.Host}; Port={configuration.World.Port}; Database={configuration.World.Database}; userid={configuration.World.Username}; Pwd={configuration.World.Password};";
            
            Map = new MapTable(connectionString);
            CreatureTemplate = new CreatureTemplateTable(connectionString);
            QuestTemplate = new QuestTemplateTable(connectionString);
            QuestReward = new QuestRewardTable(connectionString);
            QuestRewardTemplate = new QuestRewardTemplateTable(connectionString);
        }
    }
}
