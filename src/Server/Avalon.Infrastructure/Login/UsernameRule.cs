namespace Avalon.Infrastructure.Login;

/// <summary>
/// What a username may be (owner decision, #487 re-review): 3 to 16 ASCII letters, digits or
/// underscores, checked as sent, before it is trimmed or upper-cased. Registration refuses anything
/// else, and a login with anything else is answered as an unknown username. With only ASCII letters
/// allowed, upper-casing in .NET and in Postgres cannot disagree, so the database's normalised-form
/// check constraint and the code always agree.
/// </summary>
public static class UsernameRule
{
    /// <summary>The rule as a regular expression, for the request contract.</summary>
    public const string Pattern = "^[A-Za-z0-9_]{3,16}$";

    /// <summary>The message a refused registration gets.</summary>
    public const string Requirement = "Username must be 3 to 16 characters: letters A-Z, digits and underscores only.";

    public const int MinLength = 3;
    public const int MaxLength = 16;

    /// <summary>Whether <paramref name="username"/>, exactly as sent, follows the rule.</summary>
    public static bool IsValid(string? username)
    {
        if (username is null || username.Length < MinLength || username.Length > MaxLength)
            return false;

        foreach (char c in username)
        {
            bool allowed = c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_';
            if (!allowed)
                return false;
        }

        return true;
    }
}
