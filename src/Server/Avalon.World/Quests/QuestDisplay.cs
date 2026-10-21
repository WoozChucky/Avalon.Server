using Avalon.Domain.World;
using Avalon.Network.Packets.Quest;
using Avalon.World.Public.Localization;

namespace Avalon.World.Quests;

/// <summary>Builds the self-describing quest DTOs (#433) with every text resolved in the reader's language.</summary>
public static class QuestDisplay
{
    public static QuestDisplayDto Build(QuestView quest, ILocalizedTextCatalog text, TextContext context, bool completionText) => new()
    {
        Title = text.Get(quest.TitleTextId, context),
        Text = text.Get(completionText ? quest.CompletionTextId : quest.DescriptionTextId, context),
        Stages = quest.Stages.Select(stage => new QuestStageDto
        {
            Sequence = stage.Sequence,
            Text = stage.DescriptionTextId is { } id ? text.Get(id, context) : string.Empty,
            Objectives = stage.Objectives.Select(o => new QuestObjectiveDto
            {
                ObjectiveId = o.Id,
                Kind = KindOf(o.Type),
                TargetId = o.CreatureTemplateId?.Value ?? o.ItemTemplateId?.Value ?? o.DoodadTemplateId?.Value ?? 0,
                Count = o.Count,
                Text = text.Get(o.DescriptionTextId, context),
            }).ToList(),
        }).ToList(),
        Rewards = new QuestRewardsDto
        {
            Experience = quest.RewardExperience,
            Money = quest.RewardMoney,
            Items = quest.ItemRewards.Select(r => new QuestRewardItemDto { ItemTemplateId = r.ItemTemplateId.Value, Count = r.Count }).ToList(),
        },
        Repeat = RepeatKindOf(quest.Repeat),
    };

    /// <summary>The wire's name for a stored type. Explicit, never a cast.</summary>
    public static QuestObjectiveKind KindOf(QuestObjectiveType type) => type switch
    {
        QuestObjectiveType.Kill => QuestObjectiveKind.Kill,
        QuestObjectiveType.Collect => QuestObjectiveKind.Collect,
        QuestObjectiveType.Talk => QuestObjectiveKind.Talk,
        QuestObjectiveType.Scripted => QuestObjectiveKind.Scripted,
        QuestObjectiveType.UseDoodad => QuestObjectiveKind.UseDoodad,
        _ => QuestObjectiveKind.Unknown,
    };

    /// <summary>The wire's name for how a quest comes back (#710). Explicit, never a cast.</summary>
    public static QuestRepeatKind RepeatKindOf(QuestRepeat repeat) => repeat switch
    {
        QuestRepeat.Once => QuestRepeatKind.Once,
        QuestRepeat.Repeatable => QuestRepeatKind.Repeatable,
        QuestRepeat.Daily => QuestRepeatKind.Daily,
        QuestRepeat.Weekly => QuestRepeatKind.Weekly,
        QuestRepeat.Monthly => QuestRepeatKind.Monthly,
        _ => QuestRepeatKind.Unknown,
    };
}
