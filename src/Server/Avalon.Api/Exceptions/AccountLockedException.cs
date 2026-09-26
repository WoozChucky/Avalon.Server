namespace Avalon.Api.Exceptions;

/// <summary>
/// A login, MFA verify or re-authentication refused because a budget is spent or the account is
/// locked (#478): the per-source or per-username budget is past its limit, the account row is
/// locked, or this attempt's failure was the one that locked it. Answered as 429 ProblemDetails
/// with <c>Detail</c> LOCKED, as the game client is told LOCKED, and the same for every username,
/// so it says nothing about which ones exist.
/// </summary>
public sealed class AccountLockedException : Exception
{
    public AccountLockedException() : base("LOCKED")
    {
    }
}
