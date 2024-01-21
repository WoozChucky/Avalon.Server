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
        
        public WorldDatabase(DatabaseConnection configuration)
        {
            var connectionString = $"Server={configuration.Host};" +
                                   $"Port={configuration.Port};" +
                                   $"Database={configuration.Database};" +
                                   $"userid={configuration.Username};" +
                                   $"Pwd={configuration.Password};" +
                                   $"ConvertZeroDatetime=True;" +
                                   $"AllowZeroDateTime=True";
            
            Map = new MapTable(connectionString);
            CreatureTemplate = new CreatureTemplateTable(connectionString);
            QuestTemplate = new QuestTemplateTable(connectionString);
            QuestReward = new QuestRewardTable(connectionString);
            QuestRewardTemplate = new QuestRewardTemplateTable(connectionString);
        }
    }
}
