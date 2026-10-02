namespace Avalon.Domain.Characters;

/// <summary>
/// What a character name may be, how it is stored, and how it is looked up (#757). A name is 3 to 12 ASCII letters,
/// checked as sent, before anything is changed. It is stored in one display form, first letter upper-case and the rest
/// lower-case (<see cref="Display" />), and found by its key, the name upper-cased (<see cref="Key" />), which
/// <c>Characters.NameKey</c> holds under a unique index: "Bob" and "bob" are one name in a world. Only ASCII letters
/// fold, in .NET and in the database alike: Postgres <c>upper()</c> and .NET can disagree outside ASCII (a dotless i
/// upper-cases to I in .NET), and the rule keeps every stored name inside it.
/// </summary>
public static class CharacterName
{
    public const int MinLength = 3;
    public const int MaxLength = 12;

    /// <summary>Why <paramref name="name" />, exactly as sent, breaks the rule, or <see cref="CharacterNameProblem.None" />.</summary>
    public static CharacterNameProblem Check(string? name)
    {
        if (name is null || name.Length < MinLength)
            return CharacterNameProblem.TooShort;
        if (name.Length > MaxLength)
            return CharacterNameProblem.TooLong;

        foreach (char c in name)
        {
            if (!char.IsAsciiLetter(c))
                return CharacterNameProblem.Invalid;
        }

        return CharacterNameProblem.None;
    }

    /// <summary>Whether <paramref name="name" />, exactly as sent, follows the rule.</summary>
    public static bool IsValid(string? name) => Check(name) == CharacterNameProblem.None;

    /// <summary>The form a valid name is stored and shown in: first letter upper-case, the rest lower-case.</summary>
    public static string Display(string name)
    {
        if (name.Length == 0)
            return name;

        return string.Create(name.Length, name, static (span, source) =>
        {
            span[0] = AsciiUpper(source[0]);
            for (int i = 1; i < source.Length; i++)
                span[i] = AsciiLower(source[i]);
        });
    }

    /// <summary>
    /// The key a name is looked up by: trimmed, with its ASCII letters upper-cased and everything else left as it is,
    /// as SQL <c>upper()</c> does to the ASCII names the rule allows. A typed name with surrounding spaces finds the
    /// same character; one with a non-ASCII letter finds none.
    /// </summary>
    public static string Key(string name)
    {
        string trimmed = name.Trim();
        return string.Create(trimmed.Length, trimmed, static (span, source) =>
        {
            for (int i = 0; i < source.Length; i++)
                span[i] = AsciiUpper(source[i]);
        });
    }

    /// <summary>Whether two names are one name: their keys are equal.</summary>
    public static bool Same(string a, string b) => string.Equals(Key(a), Key(b), StringComparison.Ordinal);

    private static char AsciiUpper(char c) => c is >= 'a' and <= 'z' ? (char)(c - ('a' - 'A')) : c;

    private static char AsciiLower(char c) => c is >= 'A' and <= 'Z' ? (char)(c + ('a' - 'A')) : c;
}
