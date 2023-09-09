using Avalon.Database;
using Avalon.Database.World.Model;
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
    
    public QuestManager(ILogger<QuestManager> logger, IDatabaseManager databaseManager)
    {
        _logger = logger;
        _databaseManager = databaseManager;
    }

    public async void LoadQuests()
    {
        _questTemplates = _databaseManager.World.QuestTemplate.QueryAllAsync().GetAwaiter().GetResult();
        _questRewardTemplates = _databaseManager.World.QuestRewardTemplate.QueryAllAsync().GetAwaiter().GetResult();
        _questRewards = _databaseManager.World.QuestReward.QueryAllAsync().GetAwaiter().GetResult();
        
        _logger.LogInformation("Loaded {Count} quest templates", _questTemplates.Count());
        _logger.LogInformation("Loaded {Count} quest reward templates", _questRewardTemplates.Count());
        _logger.LogInformation("Loaded {Count} quest rewards", _questRewards.Count());
    }
}
