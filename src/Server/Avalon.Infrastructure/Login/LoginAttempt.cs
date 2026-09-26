using Avalon.Domain.Auth;

namespace Avalon.Infrastructure.Login;

/// <summary>
/// The slots one password or MFA-code attempt holds, and the account it named. What happens to
/// them once the caller has answered (kept, given back, or the username's count cleared) is up to
/// <see cref="LoginPolicy"/>, which the caller asks once it knows how the attempt ends.
/// </summary>
public abstract record LoginAttempt(LoginSource Source, string? UsernameKey, long Taken, Account? Account);

/// <summary>How a password check ended, in the order the checks run.</summary>
public enum PasswordCheck
{
    /// <summary>The source is past its budget; nothing was looked up. Answered as locked.</summary>
    SourceRefused,
    /// <summary>The username is past its budget; nothing was looked up. Answered as locked.</summary>
    UsernameRefused,
    /// <summary>No account has the username. One dummy BCrypt verify was paid, as for a wrong password.</summary>
    UnknownUsername,
    /// <summary>The account row is locked; the password was not checked. Answered as locked.</summary>
    Locked,
    WrongPassword,
    Correct,
}

public sealed record PasswordAttempt(PasswordCheck Result, LoginSource Source, string? UsernameKey, long Taken, Account? Account)
    : LoginAttempt(Source, UsernameKey, Taken, Account)
{
    /// <summary>Refused before the password was checked: the answer is "locked", whatever the account.</summary>
    public bool Refused => Result is PasswordCheck.SourceRefused or PasswordCheck.UsernameRefused or PasswordCheck.Locked;

    /// <summary>A wrong password, or a username no account has: answered and then recorded as a failure.</summary>
    public bool Failed => Result is PasswordCheck.UnknownUsername or PasswordCheck.WrongPassword;
}

/// <summary>How an MFA-code check ended, in the order the checks run.</summary>
public enum MfaCodeCheck
{
    /// <summary>The source is past its budget. Answered as locked.</summary>
    SourceRefused,
    /// <summary>The hash is gone (expired, spent, or never issued). The code was not checked.</summary>
    HashGone,
    /// <summary>The hash was past its attempt limit and has been deleted. The code was not checked.</summary>
    HashSpent,
    /// <summary>The hash names an account that no longer exists.</summary>
    AccountMissing,
    /// <summary>The account's username is past its budget. Answered as locked.</summary>
    UsernameRefused,
    /// <summary>The account row is locked; the code was not checked. Answered as locked.</summary>
    Locked,
    WrongCode,
    Correct,
}

public sealed record MfaCodeAttempt(MfaCodeCheck Result, LoginSource Source, string? UsernameKey, long Taken, Account? Account)
    : LoginAttempt(Source, UsernameKey, Taken, Account)
{
    /// <summary>Refused as locked before the code was checked.</summary>
    public bool Refused => Result is MfaCodeCheck.SourceRefused or MfaCodeCheck.UsernameRefused or MfaCodeCheck.Locked;
}
