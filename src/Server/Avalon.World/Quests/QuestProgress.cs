using Avalon.Domain.World;
using Avalon.World.Entities;

namespace Avalon.World.Quests;

/// <summary>
/// The real IQuestProgress (#433), over the character's own quest log: Active means held (active or ready to turn
/// in), Completed means turned in. Stateless, so one instance serves every caller.
/// </summary>
public sealed class QuestProgress : IQuestProgress
{
    public static readonly QuestProgress Instance = new();

    public bool IsMet(CharacterEntity character, uint questId, QuestRequirementState state) => state switch
    {
        QuestRequirementState.Active => character.Quests.IsActive(questId),
        QuestRequirementState.Completed => character.Quests.IsCompleted(questId),
        _ => false,
    };
}
