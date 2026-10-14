namespace Avalon.Common;

/// <summary>The classes a character can have: every <see cref="CharacterClass" /> member but None.</summary>
public static class CharacterClasses
{
    private static readonly CharacterClass[] s_playable = Enum.GetValues(typeof(CharacterClass))
        .Cast<CharacterClass>()
        .Where(c => c != CharacterClass.None)
        .ToArray();

    /// <summary>Every class a character can have, lowest number first.</summary>
    public static IReadOnlyList<CharacterClass> Playable => s_playable;

    /// <summary>Whether <paramref name="characterClass" /> is a member other than None.</summary>
    public static bool IsPlayable(CharacterClass characterClass) =>
        characterClass != CharacterClass.None && Array.IndexOf(s_playable, characterClass) >= 0;
}
