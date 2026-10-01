using Avalon.Common.ValueObjects;

namespace Avalon.Domain.Characters;

/// <summary>Where a character's active quest stands. Stored as a number, so values are only ever appended.</summary>
public enum CharacterQuestState : byte
{
    Active = 1,
    ReadyToTurnIn = 2,
}

/// <summary>One active quest of a character (#433). QuestId names a World DB QuestTemplate; no foreign key crosses databases.</summary>
public class CharacterQuest
{
    public CharacterId CharacterId { get; set; } = default!;
    public uint QuestId { get; set; }
    public CharacterQuestState State { get; set; }
    public int Stage { get; set; }
    public DateTime AcceptedAt { get; set; }
}
