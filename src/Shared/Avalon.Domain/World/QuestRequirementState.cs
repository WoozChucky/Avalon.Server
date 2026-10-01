namespace Avalon.Domain.World;

/// <summary>
/// What a quest-gated vendor row asks of the character's quest (#432): Active is held (active or
/// ready), Completed is turned in; QuestProgress answers it (#433). Stored as a number, so values
/// are only ever appended.
/// </summary>
public enum QuestRequirementState
{
    Active = 0,
    Completed = 1,
}
