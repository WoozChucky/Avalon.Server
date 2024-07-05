namespace Avalon.Domain.World;

public class QuestRewardTemplate
{
    public uint Id { get; set; }
    public string Description { get; set; }
    public QuestRewardType Type { get; set; }
    public uint Value { get; set; }
    public uint Count { get; set; }
}

// 0 = None, 1 = Item, 2 = Gold, 3 = Experience, 4 = Skill, 5 = Title, 6 = Other
public enum QuestRewardType : ushort
{
    None,
    Item,
    Gold,
    Experience,
    Skill,
    Title,
    Other
}
