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
    public static string Normalise(string email) => email.Trim().ToLowerInvariant();
}
