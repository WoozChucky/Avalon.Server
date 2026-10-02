using Avalon.Common.ValueObjects;

namespace Avalon.Domain.World;

/// <summary>
/// A Collect objective's item drops from this creature at this chance (a percentage), rolled per eligible
/// member who has the objective unmet in its current stage. The item is the objective's.
/// </summary>
public class QuestItemDrop
{
    public uint ObjectiveId { get; set; }
    public CreatureTemplateId CreatureTemplateId { get; set; } = default!;
    public float Chance { get; set; }
}
