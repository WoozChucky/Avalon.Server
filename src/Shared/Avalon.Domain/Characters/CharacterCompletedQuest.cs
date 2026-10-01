using Avalon.Common.ValueObjects;

namespace Avalon.Domain.Characters;

/// <summary>A quest a character has turned in. Never removed in v1 (repeatables are #710).</summary>
public class CharacterCompletedQuest
{
    public CharacterId CharacterId { get; set; } = default!;
    public uint QuestId { get; set; }
    public DateTime CompletedAt { get; set; }
}
