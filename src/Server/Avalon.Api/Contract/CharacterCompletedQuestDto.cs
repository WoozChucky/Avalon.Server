namespace Avalon.Api.Contract;

public sealed class CharacterCompletedQuestDto
{
    public uint QuestId { get; set; }

    /// <summary>When it was first turned in.</summary>
    public DateTime CompletedAt { get; set; }
}
