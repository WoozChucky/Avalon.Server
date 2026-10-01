using Avalon.World.Public.Enums;

namespace Avalon.Api.Contract;

/// <summary>
/// A quest as its world's data defines it (#714): who gives it and takes it back, its stages and objectives, what it
/// pays and which creatures drop its quest items. Each text is the base-locale (enUS) wording beside its text id;
/// a text id with no row reads as an empty string. The template's Environment, Type, Rarity, IsRepeatable and
/// RepeatFrequency are left out: the server does not read them in v1 (repeatables are #710).
/// </summary>
public sealed class QuestTemplateDto
{
    public uint Id { get; set; }

    public string Title { get; set; } = "";
    public int TitleTextId { get; set; }

    public string Description { get; set; } = "";
    public int DescriptionTextId { get; set; }

    /// <summary>What the ender says when the quest is handed in.</summary>
    public string CompletionText { get; set; } = "";
    public int CompletionTextId { get; set; }

    /// <summary>The creature template that offers the quest.</summary>
    public ulong GiverCreatureTemplateId { get; set; }

    /// <summary>The creature template the quest is handed in to.</summary>
    public ulong EnderCreatureTemplateId { get; set; }

    public ushort LevelRequirement { get; set; }

    /// <summary>Null: any class.</summary>
    public CharacterClass? ClassRequirement { get; set; }

    /// <summary>The quest that must be completed first, or null.</summary>
    public uint? RequiredQuestId { get; set; }

    /// <summary>A quest script type name, or null for a quest that is data only.</summary>
    public string? ScriptName { get; set; }

    public uint RewardExperience { get; set; }

    /// <summary>Copper.</summary>
    public ulong RewardMoney { get; set; }

    /// <summary>In order, from 0.</summary>
    public IList<QuestStageTemplateDto> Stages { get; set; } = [];

    /// <summary>Fixed items paid on turn-in, by item template id.</summary>
    public IList<QuestItemRewardDto> ItemRewards { get; set; } = [];

    /// <summary>Every quest-item drop of the quest's Collect objectives, by objective then creature template id.</summary>
    public IList<QuestItemDropDto> ItemDrops { get; set; } = [];
}
