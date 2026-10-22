using Avalon.Common.ValueObjects;

namespace Avalon.Domain.World;

/// <summary>One item of a quest's choose-one set (#711): the player picks exactly one of them on turn-in.</summary>
public class QuestItemChoice
{
    public QuestTemplateId QuestId { get; set; } = default!;
    public ItemTemplateId ItemTemplateId { get; set; } = default!;
    public uint Count { get; set; }
}
