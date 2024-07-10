using Avalon.World.Public.Enums;

namespace Avalon.Domain.World;

public class QuestRewardTemplate
{
    public uint Id { get; set; }
    public string Description { get; set; }
    public QuestRewardType Type { get; set; }
    public uint Value { get; set; }
    public uint Count { get; set; }
}
