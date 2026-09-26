namespace Avalon.Domain.World;

/// <summary>
/// What a quest-gated vendor row asks of the character's quest (#432). Stored as a number, so
/// values are only ever appended. Nothing can meet either until quests exist (#433).
/// </summary>
public enum QuestRequirementState
{
    Active = 0,
    Completed = 1,
}
