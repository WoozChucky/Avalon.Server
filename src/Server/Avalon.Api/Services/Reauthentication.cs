using System.Net;
using System.Security.Authentication;
using Avalon.Api.Exceptions;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Infrastructure.Login;

namespace Avalon.Api.Services;

/// <summary>
/// The current-password check in front of the actions a stolen session must not be able to take
/// on its own (#478, #483): changing the password, enrolling MFA, minting a personal access token.
/// It runs the login policy, so a wrong password here is a failed login in every respect: it spends
/// the source's and the username's budgets, counts on the account row and, in the budget's last
/// slot, locks the account.
/// </summary>
public interface IReauthentication
{
    /// <summary>
    /// Returns when <paramref name="password"/> is the account's current password, with the
    /// credentials version of the row the password was checked against: a credential issued on the
    /// strength of it is refused once the account's version has moved (#495). Throws
    /// <see cref="AuthenticationException"/> ("Invalid current password", 401) for a wrong or empty
    /// one, and <see cref="AccountLockedException"/> (429 LOCKED) when a budget is spent, the account
    /// is locked, or this failure locked it.
    /// </summary>
    Task<Reauthenticated> RequireCurrentPasswordAsync(AccountId accountId, string password, IPAddress address,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A passed current-password check: whose password it was, and the credentials version of the row
/// the password was verified against (#495).
/// </summary>
public readonly record struct Reauthenticated(AccountId AccountId, int CredentialsVersion);

public sealed class Reauthentication : IReauthentication
{
    public const string InvalidPassword = "Invalid current password";

    private readonly IAccountRepository _accounts;
    private readonly PasswordLoginPolicy _policy;

    public Reauthentication(IAccountRepository accounts, PasswordLoginPolicy policy)
    {
        _accounts = accounts;
        _policy = policy;
    }

    public async Task<Reauthenticated> RequireCurrentPasswordAsync(AccountId accountId, string password,
        IPAddress address, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new AuthenticationException(InvalidPassword);

        var account = await _accounts.FindByIdAsync(accountId, track: false, cancellationToken)
                      ?? throw new AuthenticationException(InvalidPassword);

        PasswordAttempt attempt = await _policy.CheckAsync(account, password, LoginSource.FromAddress(address),
            cancellationToken);

        if (attempt.Refused)
            throw new AccountLockedException();

        if (attempt.Result != PasswordCheck.Correct)
        {
            await _policy.RecordFailureAsync(attempt, cancellationToken);
            if (_policy.FailureLocks(attempt))
                throw new AccountLockedException();
            throw new AuthenticationException(InvalidPassword);
        }

        // Proved, but no login completed: only this attempt's own slots come back.
        await _policy.GiveBackAsync(attempt);
        // The version of the very row whose verifier the password matched: a change committed
        // after this read, however soon, moves the account past it.
        return new Reauthenticated(accountId, account.CredentialsVersion);
    }
}
