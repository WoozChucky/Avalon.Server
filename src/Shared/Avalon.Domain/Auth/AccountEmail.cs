namespace Avalon.Domain.Auth;

/// <summary>
/// The one form an account's email is stored and looked up in (#503): trimmed, and the whole
/// address lower-cased (<see cref="string.ToLowerInvariant"/>), local part included. Mailbox
/// providers almost never treat the local part as case-sensitive, and folding it too means
/// <c>Player@x.com</c> and <c>player@x.com</c> cannot belong to two accounts. <c>Accounts.Email</c>
/// has a unique index on this form, and a check constraint holds every writer to it.
/// </summary>
public static class AccountEmail
{
    public const string Requirement = "Email must be a valid address made of ASCII characters only";

    public static string Normalise(string email) => email.Trim().ToLowerInvariant();

    /// <summary>
    /// Whether <paramref name="email"/>, once normalised, is an address an account may hold: printable
    /// ASCII only (no spaces, no control characters), with exactly one <c>@</c> that is neither
    /// first nor last. ASCII only because .NET and Postgres lower-case ASCII identically; outside
    /// it they can disagree, and the check constraint would refuse what the code stored.
    /// </summary>
    public static bool IsValid(string? email)
    {
        if (email is null)
            return false;

        string normalised = Normalise(email);
        int at = normalised.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0 || at == normalised.Length - 1 || normalised.IndexOf('@', at + 1) >= 0)
            return false;

        foreach (char c in normalised)
        {
            if (c < '!' || c > '~')
                return false;
        }

        return true;
    }
}
