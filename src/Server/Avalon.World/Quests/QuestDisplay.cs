using Avalon.Common;
using Avalon.Domain.World;
using Avalon.Network.Packets.Quest;
using Avalon.World.Public.Localization;
using Avalon.World.Reload;

namespace Avalon.World.Quests;

/// <summary>
/// Builds the self-describing quest DTOs (#433) with every text resolved in the reader's language, and each reward item
/// at the rarity it is paid at (#1179), read from <c>items</c>, the item templates of the same tick.
/// </summary>
public static class QuestDisplay
{
    public static QuestDisplayDto Build(QuestView quest, ItemsPatch items, ILocalizedTextCatalog text, TextContext context,
        bool completionText)
    {
        IReadOnlyList<QuestItemRewardView> fixedItems = quest.ItemRewards;
        var paid = new List<QuestRewardItemDto>(fixedItems.Count);
        for (int i = 0; i < fixedItems.Count; i++)
            paid.Add(Reward(fixedItems[i], items));

        var rewards = new QuestRewardsDto
        {
            Experience = quest.RewardExperience,
            Gold = quest.RewardGold,
            Items = paid,
        };

        // The choice set (#711), walked by index. Most quests have none, and the DTO's own empty list then stands:
        // a display is built for every offer, every accept and every held quest of a log.
        IReadOnlyList<QuestItemRewardView> choices = quest.ItemChoices;
        if (choices.Count > 0)
        {
            var picks = new List<QuestRewardItemDto>(choices.Count);
            for (int i = 0; i < choices.Count; i++)
            {
                picks.Add(Reward(choices[i], items));
            }

            rewards.Choices = picks;
        }

        return new QuestDisplayDto
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
            Rewards = rewards,
            Repeat = RepeatKindOf(quest.Repeat),
        };
    }

    /// <summary>
    /// A reward item as the window shows it: Magic for a Common base, which the turn-in rolls (B-R22), else its template's
    /// rarity; Common for a template a reload removed, which no turn-in pays.
    /// </summary>
    private static QuestRewardItemDto Reward(QuestItemRewardView reward, ItemsPatch items)
    {
        ItemTemplate? template = items.Find(reward.ItemTemplateId);
        return new QuestRewardItemDto
        {
            ItemTemplateId = reward.ItemTemplateId.Value,
            Count = reward.Count,
            Rarity = template is null ? ItemRarity.Common
                : QuestTurnInRules.RollsMagic(template) ? ItemRarity.Magic
                : template.Rarity,
        };
    }

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
