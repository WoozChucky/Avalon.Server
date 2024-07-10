using Avalon.World.Public.Enums;

namespace Avalon.Domain.World;

public class QuestTemplate
{
    public uint Id { get; set; }
    public QuestEnvironmentType Environment { get; set; }
    public QuestType Type { get; set; }
    public QuestRarity Rarity { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int GiverCreatureId { get; set; }
    public int EnderCreatureId { get; set; }
    public int CompletionCriteriaId { get; set; }
    public bool IsRepeatable { get; set; }
    public QuestRepeatFrequency? RepeatFrequency { get; set; }
    public int LevelRequirement { get; set; }
    public int RequiredQuestId { get; set; }
    public int ClassRequirement { get; set; }
    public ICollection<QuestReward> Rewards { get; set; } = new List<QuestReward>();
}
