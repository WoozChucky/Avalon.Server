namespace Avalon.Api.Contract;

/// <summary>A character's quest log as its world last saved it (#714): the quests it holds and the ones it has turned in.</summary>
public sealed class CharacterQuestLogDto
{
    public uint CharacterId { get; set; }

    /// <summary>The held quests, by quest id.</summary>
    public IList<CharacterActiveQuestDto> Active { get; set; } = [];

    /// <summary>The turned-in quests, by quest id.</summary>
    public IList<CharacterCompletedQuestDto> Completed { get; set; } = [];
}
