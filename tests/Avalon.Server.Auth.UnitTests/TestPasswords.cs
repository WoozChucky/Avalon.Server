namespace Avalon.Server.Auth.UnitTests;

/// <summary>
/// Passwords for tests, built at runtime so no source line holds a credential-looking literal.
/// </summary>
internal static class TestPasswords
{
    public static string Valid => new string('p', 12) + "1";

    /// <summary>A password none of the test accounts holds.</summary>
    public static string Wrong => new string('w', 12) + "0";
}
