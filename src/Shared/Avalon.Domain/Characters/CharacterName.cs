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

    /// <summary>The rule as a regular expression, for the REST request contract and its OpenAPI schema.</summary>
    public const string Pattern = "^[A-Za-z]{3,12}$";

    /// <summary>The message a name that breaks the rule gets from the REST API.</summary>
    public const string Requirement = "Character name must be 3 to 12 letters A-Z only.";

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
    /// The key a name is stored under (<c>Characters.NameKey</c>): its ASCII letters upper-cased and everything else
    /// left as it is, nothing trimmed, exactly what the check constraint's <c>upper("Name" COLLATE "C")</c> gives, so
    /// the two agree on every name. A name with a non-ASCII letter keeps it, and never finds an ASCII name.
    /// </summary>
    public static string Key(string name) =>
        string.Create(name.Length, name, static (span, source) =>
        {
            for (int i = 0; i < source.Length; i++)
                span[i] = AsciiUpper(source[i]);
        });

    /// <summary>The key a typed name is looked up by: <see cref="Key" /> of it trimmed, so surrounding spaces are ignored.</summary>
    public static string LookupKey(string name) => Key(name.Trim());

    /// <summary>Whether two names, as typed, are one name: their lookup keys are equal.</summary>
    public static bool Same(string a, string b) => string.Equals(LookupKey(a), LookupKey(b), StringComparison.Ordinal);

    private static char AsciiUpper(char c) => c is >= 'a' and <= 'z' ? (char)(c - ('a' - 'A')) : c;

    private static char AsciiLower(char c) => c is >= 'A' and <= 'Z' ? (char)(c + ('a' - 'A')) : c;
}
