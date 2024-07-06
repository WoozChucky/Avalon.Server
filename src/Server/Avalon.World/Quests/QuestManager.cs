using Avalon.Domain.World;
using Avalon.Game;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Quests;

public interface IQuestManager
{
    void LoadQuests();
}

public class QuestManager : IQuestManager
{
    //private readonly IQuestTemplateRepository _questTemplateRepository;
    //private readonly IQuestRewardTemplateRepository _questRewardTemplateRepository;
    //private readonly IQuestRewardRepository _questRewardRepository;
    private readonly ILogger<QuestManager> _logger;

    private IEnumerable<QuestTemplate> _questTemplates;
    private IEnumerable<QuestRewardTemplate> _questRewardTemplates;
    private IEnumerable<QuestReward> _questRewards;
    
    public QuestManager(ILoggerFactory loggerFactory) 
        //IQuestTemplateRepository questTemplateRepository, 
        //IQuestRewardTemplateRepository questRewardTemplateRepository, 
        //IQuestRewardRepository questRewardRepository)
    {
        //_questTemplateRepository = questTemplateRepository;
        //_questRewardTemplateRepository = questRewardTemplateRepository;
        //_questRewardRepository = questRewardRepository;
        _logger = loggerFactory.CreateLogger<QuestManager>();
        _questTemplates = new List<QuestTemplate>();
        _questRewardTemplates = new List<QuestRewardTemplate>();
        _questRewards = new List<QuestReward>();
    }

    public void LoadQuests()
    {
        //_questTemplates = _questTemplateRepository.FindAllAsync().GetAwaiter().GetResult();
        //_questRewardTemplates = _questRewardTemplateRepository.FindAllAsync().GetAwaiter().GetResult();
        //_questRewards = _questRewardRepository.FindAllAsync().GetAwaiter().GetResult();
        
        _logger.LogInformation("Loaded {Count} quest templates", _questTemplates.Count());
        _logger.LogInformation("Loaded {Count} quest reward templates", _questRewardTemplates.Count());
        _logger.LogInformation("Loaded {Count} quest rewards", _questRewards.Count());
    }
    
    public QuestTemplate? GetQuestTemplate(int id)
    {
        return _questTemplates.FirstOrDefault(q => q.Id == id);
    }
    
    public IEnumerable<QuestTemplate> GetQuestsAvailable(AvalonWorldSession session)
    {
        var mapId = session.Character?.Map;
        return _questTemplates.Where(q => q.LevelRequirement <= session.Character!.Level);
    }
}
