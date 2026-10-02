namespace Avalon.Api.Contract;

public sealed class CharacterActiveQuestDto
{
    public uint QuestId { get; set; }
    public CharacterQuestState State { get; set; }

    /// <summary>The current stage's sequence.</summary>
    public int Stage { get; set; }

    public DateTime AcceptedAt { get; set; }

    /// <summary>The saved counts, by objective id; an objective with no entry stands at 0.</summary>
    public IList<CharacterQuestObjectiveProgressDto> Objectives { get; set; } = [];
}
