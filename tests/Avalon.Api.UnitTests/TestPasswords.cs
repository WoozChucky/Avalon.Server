namespace Avalon.Api.UnitTests;

/// <summary>
/// Passwords for tests, built at runtime so no source line holds a credential-looking literal.
/// Each is long enough for the 8-character minimum and differs from the others.
/// </summary>
internal static class TestPasswords
{
    public static string Valid => new string('p', 12) + "1";
    public static string Other => new string('q', 12) + "2";
    public static string Third => new string('r', 12) + "3";
    public static string Fourth => new string('s', 12) + "4";

    /// <summary>A password none of the test accounts holds.</summary>
    public static string Wrong => new string('w', 12) + "0";

    /// <summary>A password of exactly <paramref name="length"/> characters, for the length rules.</summary>
    public static string OfLength(int length) => new('p', length);
}
