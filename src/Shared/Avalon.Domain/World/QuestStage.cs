using Avalon.Common.ValueObjects;

namespace Avalon.Domain.World;

/// <summary>One stage of a quest. Stages run 0..n in order; all of a stage's objectives complete it.</summary>
public class QuestStage
{
    public QuestTemplateId QuestId { get; set; } = default!;
    public int Sequence { get; set; }
    public LocalizedTextId? DescriptionTextId { get; set; }
}
