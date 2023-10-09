using Avalon.Database;
using Avalon.Database.World.Model;
using Avalon.Game.Maps;
using Microsoft.Extensions.Logging;

namespace Avalon.Game.Quests;

public interface IQuestManager
{
    void LoadQuests();
}

public class QuestManager : IQuestManager
{
    private readonly ILogger<QuestManager> _logger;
    private readonly IDatabaseManager _databaseManager;

    private IEnumerable<QuestTemplate> _questTemplates;
    private IEnumerable<QuestRewardTemplate> _questRewardTemplates;
    private IEnumerable<QuestReward> _questRewards;
    
    public QuestManager(ILoggerFactory loggerFactory, IDatabaseManager databaseManager)
    {
        _logger = loggerFactory.CreateLogger<QuestManager>();
        _databaseManager = databaseManager;
        _questTemplates = new List<QuestTemplate>();
        _questRewardTemplates = new List<QuestRewardTemplate>();
        _questRewards = new List<QuestReward>();
    }

    public void LoadQuests()
    {
        _questTemplates = _databaseManager.World.QuestTemplate.QueryAllAsync().GetAwaiter().GetResult();
        _questRewardTemplates = _databaseManager.World.QuestRewardTemplate.QueryAllAsync().GetAwaiter().GetResult();
        _questRewards = _databaseManager.World.QuestReward.QueryAllAsync().GetAwaiter().GetResult();
        
        _logger.LogInformation("Loaded {Count} quest templates", _questTemplates.Count());
        _logger.LogInformation("Loaded {Count} quest reward templates", _questRewardTemplates.Count());
        _logger.LogInformation("Loaded {Count} quest rewards", _questRewards.Count());
    }
    
    public QuestTemplate? GetQuestTemplate(int id)
    {
        return _questTemplates.FirstOrDefault(q => q.Id == id);
    }
    
    public IEnumerable<QuestTemplate> GetQuestsAvailable(AvalonSession session)
    {
        var mapId = session.Character?.Map;
        return _questTemplates.Where(q => q.LevelRequirement <= session.Character!.Level);
    }
}
