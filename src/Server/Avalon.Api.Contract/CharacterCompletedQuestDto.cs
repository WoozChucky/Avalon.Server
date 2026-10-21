namespace Avalon.Api.Contract;

public sealed class CharacterCompletedQuestDto
{
    public uint QuestId { get; set; }

    /// <summary>When it was first turned in.</summary>
    public DateTime CompletedAt { get; set; }

    /// <summary>When it was last turned in (#710); the first turn-in for a quest done once.</summary>
    public DateTime LastCompletedAt { get; set; }

    /// <summary>How many times it was turned in (#710).</summary>
    public uint CompletionCount { get; set; }
}
