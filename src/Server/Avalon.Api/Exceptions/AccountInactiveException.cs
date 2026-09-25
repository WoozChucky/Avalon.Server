using Avalon.Domain.Auth;

namespace Avalon.Api.Exceptions;

/// <summary>
/// A caller proved they hold the account (right password, or right MFA code), but the account is
/// not Active. Answered as 403 with the status, BANNED or DEACTIVATED, as the game client's login
/// is (#480). Throw it only after the proof: before it, a refusal must not say anything about the
/// account.
/// </summary>
public sealed class AccountInactiveException : Exception
{
    public AccountInactiveException(AccountStatus status)
        : base(status == AccountStatus.Deactivated ? "DEACTIVATED" : "BANNED")
    {
        Status = status;
    }

    public AccountStatus Status { get; }
}
