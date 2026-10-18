using Avalon.Common.ValueObjects;

namespace Avalon.Domain.World;

/// <summary>What an objective counts. Stored as a number, so values are only ever appended.</summary>
public enum QuestObjectiveType
{
    Kill = 1,
    Collect = 2,
    Talk = 3,
    Scripted = 4,

    /// <summary>Use a doodad of the named template: each use by the character counts one.</summary>
    UseDoodad = 5,
}

/// <summary>
/// One objective of a quest stage. A check constraint pins the target per type: Kill and Talk name a
/// creature template, Collect an item template, UseDoodad a doodad template, Scripted none.
/// </summary>
public class QuestObjective
{
    public uint Id { get; set; }
    public QuestTemplateId QuestId { get; set; } = default!;
    public int StageSequence { get; set; }
    public QuestObjectiveType Type { get; set; }
    public CreatureTemplateId? CreatureTemplateId { get; set; }
    public ItemTemplateId? ItemTemplateId { get; set; }

    /// <summary>For a UseDoodad objective: the doodad template whose use counts.</summary>
    public DoodadTemplateId? DoodadTemplateId { get; set; }

    public uint Count { get; set; }
    public LocalizedTextId DescriptionTextId { get; set; } = default!;

    /// <summary>For a Collect objective: the creatures its item drops from, only for characters who need it.</summary>
    public List<QuestItemDrop> Drops { get; set; } = [];
}
