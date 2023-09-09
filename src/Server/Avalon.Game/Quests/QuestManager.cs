using Avalon.Database.World;

namespace Avalon.Game.Quests;

public interface IQuestManager
{
    
}

public class QuestManager : IQuestManager
{
    private readonly IQuestTemplateTable _questTemplateTable;

    public QuestManager()
    {
        _questTemplateTable = new QuestTemplateTable();
    }
}
