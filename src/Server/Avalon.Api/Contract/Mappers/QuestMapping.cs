using Avalon.Domain.World;

namespace Avalon.Api.Contract.Mappers;

/// <summary>Quest templates as the REST contract carries them (#714).</summary>
public static class QuestMapping
{
    /// <summary>Every text a quest names: its header, its stages' lines and its objectives' lines.</summary>
    public static IEnumerable<int> TextIdsOf(QuestTemplate q) =>
        new[] { q.TitleTextId.Value, q.DescriptionTextId.Value, q.CompletionTextId.Value }
            .Concat(q.Stages.Where(s => s.DescriptionTextId is not null).Select(s => s.DescriptionTextId!.Value))
            .Concat(q.Objectives.Select(o => o.DescriptionTextId.Value));

    /// <param name="texts">Base-locale wording by text id; an id missing from it reads as an empty string.</param>
    public static QuestTemplateDto ToDto(this QuestTemplate q, IReadOnlyDictionary<int, string> texts) => new()
    {
        Id = q.Id.Value,
        Title = Text(texts, q.TitleTextId.Value),
        TitleTextId = q.TitleTextId.Value,
        Description = Text(texts, q.DescriptionTextId.Value),
        DescriptionTextId = q.DescriptionTextId.Value,
        CompletionText = Text(texts, q.CompletionTextId.Value),
        CompletionTextId = q.CompletionTextId.Value,
        GiverCreatureTemplateId = q.GiverCreatureId.Value,
        EnderCreatureTemplateId = q.EnderCreatureId.Value,
        LevelRequirement = q.LevelRequirement,
        ClassRequirement = q.ClassRequirement,
        RequiredQuestId = q.RequiredQuestId?.Value,
        ScriptName = q.ScriptName,
        RewardExperience = q.RewardExperience,
        RewardMoney = q.RewardMoney,
        Stages = q.Stages.OrderBy(s => s.Sequence).Select(s => new QuestStageTemplateDto
        {
            Sequence = s.Sequence,
            Text = s.DescriptionTextId is { } id ? Text(texts, id.Value) : null,
            TextId = s.DescriptionTextId?.Value,
            Objectives = q.Objectives.Where(o => o.StageSequence == s.Sequence).OrderBy(o => o.Id)
                .Select(o => new QuestObjectiveTemplateDto
                {
                    Id = o.Id,
                    Type = (Avalon.Api.Contract.QuestObjectiveType)o.Type,
                    CreatureTemplateId = o.CreatureTemplateId?.Value,
                    ItemTemplateId = o.ItemTemplateId?.Value,
                    Count = o.Count,
                    Text = Text(texts, o.DescriptionTextId.Value),
                    TextId = o.DescriptionTextId.Value,
                }).ToList(),
        }).ToList(),
        ItemRewards = q.ItemRewards.OrderBy(r => r.ItemTemplateId.Value)
            .Select(r => new QuestItemRewardDto { ItemTemplateId = r.ItemTemplateId.Value, Count = r.Count })
            .ToList(),
        ItemDrops = q.Objectives.SelectMany(o => o.Drops)
            .OrderBy(d => d.ObjectiveId).ThenBy(d => d.CreatureTemplateId.Value)
            .Select(d => new QuestItemDropDto
            {
                ObjectiveId = d.ObjectiveId,
                CreatureTemplateId = d.CreatureTemplateId.Value,
                Chance = d.Chance,
            })
            .ToList(),
    };

    private static string Text(IReadOnlyDictionary<int, string> texts, int id) =>
        texts.TryGetValue(id, out string? text) ? text : "";
}
