namespace Avalon.Api.Contract;

/// <summary>
/// How many of a character's worn items carry a prefix or suffix of one Resonance theme (#1180), each item counted once
/// per theme however many of its affixes share it.
/// </summary>
public sealed class CharacterResonanceDto
{
    /// <summary>The theme, by its id in the world's catalog.</summary>
    public int ThemeId { get; set; }
    /// <summary>The worn items that carry it, at least 1.</summary>
    public int Count { get; set; }
}
