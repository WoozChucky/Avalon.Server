using Avalon.Database.Character.Repositories;

namespace Avalon.Api.Contract.Mappers;

/// <summary>A character's saved quest rows as the REST contract carries them (#714).</summary>
public static class CharacterQuestLogMapping
{
    public static CharacterQuestLogDto ToQuestLogDto(this CharacterQuestRows rows,
        uint characterId) => new()
    {
        CharacterId = characterId,
        Active = rows.Active.OrderBy(q => q.QuestId).Select(q => new CharacterActiveQuestDto
        {
            QuestId = q.QuestId,
            State = (Avalon.Api.Contract.CharacterQuestState)q.State,
            Stage = q.Stage,
            AcceptedAt = q.AcceptedAt,
            Objectives = rows.Objectives.Where(o => o.QuestId == q.QuestId).OrderBy(o => o.ObjectiveId)
                .Select(o => new CharacterQuestObjectiveProgressDto { ObjectiveId = o.ObjectiveId, Progress = o.Progress })
                .ToList(),
        }).ToList(),
        Completed = rows.Completed.OrderBy(c => c.QuestId)
            .Select(c => new CharacterCompletedQuestDto { QuestId = c.QuestId, CompletedAt = c.CompletedAt })
            .ToList(),
    };
}
