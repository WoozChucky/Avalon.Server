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
    public static CharacterNameProblem Check(string? name) => Check(name.AsSpan());

    /// <summary>Why <paramref name="name" />, exactly as sent, breaks the rule, or <see cref="CharacterNameProblem.None" />.</summary>
    public static CharacterNameProblem Check(ReadOnlySpan<char> name)
    {
        if (name.Length < MinLength)
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

    /// <summary>Whether <paramref name="name" />, exactly as sent, follows the rule.</summary>
    public static bool IsValid(ReadOnlySpan<char> name) => Check(name) == CharacterNameProblem.None;

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
    public static bool Same(string a, string b) => Same(a.AsSpan(), b.AsSpan());

    /// <summary>
    /// Whether two names, as typed, are one name: their lookup keys are equal, compared in place, so no key is made
    /// (#973).
    /// </summary>
    public static bool Same(ReadOnlySpan<char> a, ReadOnlySpan<char> b) => SameKey(a.Trim(), b.Trim());

    /// <summary>Whether two names have one <see cref="Key" />: the same length, and each character the same once ASCII letters are upper-cased.</summary>
    private static bool SameKey(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        if (a.Length != b.Length)
            return false;

        for (int i = 0; i < a.Length; i++)
        {
            if (AsciiUpper(a[i]) != AsciiUpper(b[i]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Compares names as their <see cref="Key" />s compare, without making one: ASCII letters in any case, every other
    /// character as it is, nothing trimmed. A dictionary of names under it finds a typed name as a key would, and looks
    /// one up by a slice of a message (<see cref="IAlternateEqualityComparer{TAlternate,T}" />), so a lookup makes no
    /// string (#973).
    /// </summary>
    public sealed class KeyComparer : IEqualityComparer<string>, IAlternateEqualityComparer<ReadOnlySpan<char>, string>
    {
        private KeyComparer()
        {
        }

        /// <summary>The one instance: it holds nothing.</summary>
        public static KeyComparer Instance { get; } = new();

        public bool Equals(string? x, string? y) =>
            x is null || y is null ? ReferenceEquals(x, y) : SameKey(x, y);

        public int GetHashCode(string obj) => GetHashCode(obj.AsSpan());

        // Names with one key are equal ignoring case, so they hash alike under OrdinalIgnoreCase, which folds more than
        // ASCII: a coarser hash, never a different one for equal names.
        public int GetHashCode(ReadOnlySpan<char> alternate) =>
            string.GetHashCode(alternate, StringComparison.OrdinalIgnoreCase);

        public bool Equals(ReadOnlySpan<char> alternate, string other) => SameKey(alternate, other);

        public string Create(ReadOnlySpan<char> alternate) => alternate.ToString();
    }

    private static char AsciiUpper(char c) => c is >= 'a' and <= 'z' ? (char)(c - ('a' - 'A')) : c;

    private static char AsciiLower(char c) => c is >= 'A' and <= 'Z' ? (char)(c + ('a' - 'A')) : c;
}
