using Avalon.Common.ValueObjects;

namespace Avalon.Domain.World;

/// <summary>An item a quest pays on turn-in. Fixed: choice rewards are #711.</summary>
public class QuestItemReward
{
    public QuestTemplateId QuestId { get; set; } = default!;
    public ItemTemplateId ItemTemplateId { get; set; } = default!;
    public uint Count { get; set; }
}
