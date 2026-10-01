using Avalon.Common.ValueObjects;
using Avalon.World.Public.Enums;

namespace Avalon.Domain.World;

/// <summary>
/// A quest (#433): who gives it and who takes it back, what it asks for, and what it pays. Reference data,
/// written only by migrations. Its stages and objectives are ordered; QuestCatalog validates the whole.
/// </summary>
public class QuestTemplate
{
    public QuestTemplateId Id { get; set; } = default!;

    // Kept, unread in v1 (#710 repeatables and dailies will read the repeat pair).
    public QuestEnvironmentType Environment { get; set; }
    public QuestType Type { get; set; }
    public QuestRarity Rarity { get; set; }
    public bool IsRepeatable { get; set; }
    public QuestRepeatFrequency? RepeatFrequency { get; set; }

    public LocalizedTextId TitleTextId { get; set; } = default!;
    public LocalizedTextId DescriptionTextId { get; set; } = default!;

    /// <summary>What the ender says when the quest is handed in.</summary>
    public LocalizedTextId CompletionTextId { get; set; } = default!;

    public CreatureTemplateId GiverCreatureId { get; set; } = default!;
    public CreatureTemplateId EnderCreatureId { get; set; } = default!;

    public ushort LevelRequirement { get; set; }

    /// <summary>Null: any class.</summary>
    public CharacterClass? ClassRequirement { get; set; }

    /// <summary>The one quest that must be completed first, or null.</summary>
    public QuestTemplateId? RequiredQuestId { get; set; }

    /// <summary>Personal: no party split, no level gap, no map band.</summary>
    public uint RewardExperience { get; set; }

    /// <summary>Copper.</summary>
    public ulong RewardMoney { get; set; }

    /// <summary>A QuestScript type name, or null for a quest that is data only.</summary>
    public string? ScriptName { get; set; }

    public List<QuestStage> Stages { get; set; } = [];
    public List<QuestObjective> Objectives { get; set; } = [];
    public List<QuestItemReward> ItemRewards { get; set; } = [];
}
