namespace Avalon.Database.World
{
    public interface IWorldDatabase
    {
        IMapRepository Map { get; }
        ICreatureTemplateRepository CreatureTemplate { get; }
        IQuestTemplateRepository QuestTemplate { get; }
        IQuestRewardRepository QuestReward { get; }
        IQuestRewardTemplateRepository QuestRewardTemplate { get; }
    }
    
    public class WorldDatabase : IWorldDatabase
    {
        public IMapRepository Map { get; }
        public ICreatureTemplateRepository CreatureTemplate { get; }
        public IQuestTemplateRepository QuestTemplate { get; }
        public IQuestRewardRepository QuestReward { get; }
        public IQuestRewardTemplateRepository QuestRewardTemplate { get; }
        
        public WorldDatabase(IMapRepository mapRepository,
                             ICreatureTemplateRepository creatureTemplateRepository,
                             IQuestTemplateRepository questTemplateRepository,
                             IQuestRewardRepository questRewardRepository,
                             IQuestRewardTemplateRepository questRewardTemplateRepository)
        {
            Map = mapRepository;
            CreatureTemplate = creatureTemplateRepository;
            QuestTemplate = questTemplateRepository;
            QuestReward = questRewardRepository;
            QuestRewardTemplate = questRewardTemplateRepository;
        }
    }
}
