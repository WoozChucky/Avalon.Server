using Avalon.Common.ValueObjects;

namespace Avalon.Domain.Characters;

/// <summary>A quest a character has turned in at least once (#710: repeatables keep one row and count).</summary>
public class CharacterCompletedQuest
{
    public CharacterId CharacterId { get; set; } = default!;
    public uint QuestId { get; set; }

    /// <summary>The first turn-in.</summary>
    public DateTime CompletedAt { get; set; }

    /// <summary>The most recent turn-in; what availability compares against.</summary>
    public DateTime LastCompletedAt { get; set; }

    /// <summary>How many turn-ins.</summary>
    public uint CompletionCount { get; set; }
}
