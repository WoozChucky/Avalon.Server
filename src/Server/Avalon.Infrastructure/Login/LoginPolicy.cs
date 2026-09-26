using System.Text;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace Avalon.Infrastructure.Login;

/// <summary>
/// The login rules both servers enforce (#478), so the game client's TCP login and the REST API's
/// cannot drift apart again: the per-source and per-username budgets taken before any work, the
/// account lock checked before the proof, failures counted in SQL and, in the budget's last slot,
/// locking the account. The Redis keys are the same on both servers (<see cref="SourceBudget"/>,
/// <see cref="UsernameBudget"/>), so a guess over either spends the same budget. The caller answers
/// each outcome in its own protocol, then tells the policy how the attempt ended: a failure
/// (<see cref="RecordFailureAsync"/>), a pause on the way to a login (<see cref="GiveBackAsync"/>),
/// or a completed login (<see cref="CompleteAsync"/>). Every other ending keeps both slots.
/// </summary>
public abstract class LoginPolicy
{
    /// <summary>
    /// The id a failure is recorded against when no account has the username, by a caller that
    /// cannot answer before its write (the REST API): the same statements run, and match no row, so
    /// an unknown username takes the same path as a known one. No account has id 0; ids start at 1.
    /// </summary>
    public static readonly AccountId NoAccount = new(0);

    protected LoginPolicy(IAccountRepository accounts, IReplicatedCache cache, ILoginLimits limits, ILogger logger)
    {
        Accounts = accounts;
        Cache = cache;
        Limits = limits;
        Logger = logger;
    }

    protected IAccountRepository Accounts { get; }
    protected IReplicatedCache Cache { get; }
    protected ILoginLimits Limits { get; }
    protected ILogger Logger { get; }

    /// <summary>Whether a failure in this attempt's slot locks the account, and so is answered as locked.</summary>
    public bool FailureLocks(LoginAttempt attempt) => UsernameBudget.Locks(Limits, attempt.Taken);

    /// <summary>
    /// Records a failure once it has been answered (or, for a caller that cannot answer first,
    /// just before): holds the username's budget when the failure is in the last slot, and counts
    /// it on the account row, locking the row with it. A username no account has writes no row,
    /// unless <paramref name="writeWithoutAccount"/> asks for the same statements against
    /// <see cref="NoAccount"/>. See <see cref="UsernameBudget.RecordFailureAsync"/>.
    /// </summary>
    public Task RecordFailureAsync(LoginAttempt attempt, CancellationToken token, bool writeWithoutAccount = false)
    {
        AccountId? accountId = attempt.Account?.Id ?? (writeWithoutAccount ? NoAccount : null);
        string ip = attempt.Source.Ip;
        return UsernameBudget.RecordFailureAsync(Cache, Limits, Logger, attempt.UsernameKey!, attempt.Taken,
            accountId == null
                ? null
                : (now, lockUntil, writeToken) => Accounts.RecordFailedLoginAsync(accountId, ip, now, lockUntil, writeToken),
            token);
    }

    /// <summary>
    /// A right proof that does not complete a login (an MFA hash issued, a re-authentication):
    /// both slots back, and no reset, since the login, if any, is not complete yet.
    /// </summary>
    public async Task GiveBackAsync(LoginAttempt attempt)
    {
        await SourceBudget.GiveBackAsync(Cache, attempt.Source.Key);
        await UsernameBudget.GiveBackAsync(Cache, Limits, attempt.UsernameKey!);
    }

    /// <summary>
    /// A completed login, once it is recorded: the source gets its own slot back, and the
    /// username's count is cleared (owner decision on #484), unless a hold is on it.
    /// </summary>
    public async Task CompleteAsync(LoginAttempt attempt)
    {
        await SourceBudget.GiveBackAsync(Cache, attempt.Source.Key);
        await UsernameBudget.ResetAsync(Cache, Limits, attempt.UsernameKey!);
    }

    /// <summary>Takes the source's slot. False when the source is past its budget.</summary>
    protected async Task<bool> TakeSourceAsync(LoginSource source, string what)
    {
        if (await SourceBudget.TryTakeAsync(Cache, Limits, source.Key))
            return true;

        Logger.LogWarning("{What} refused for source {SourceKey}: too many failed attempts", what, source.Key);
        return false;
    }
}

/// <summary>The password step: a login by username, or a re-authentication of a signed-in account.</summary>
public sealed class PasswordLoginPolicy : LoginPolicy
{
    private readonly IPasswordVerifier _verifier;

    public PasswordLoginPolicy(IAccountRepository accounts, IReplicatedCache cache, ILoginLimits limits,
        IPasswordVerifier verifier, ILoggerFactory loggerFactory)
        : base(accounts, cache, limits, loggerFactory.CreateLogger<PasswordLoginPolicy>())
    {
        _verifier = verifier;
    }

    /// <summary>
    /// Checks a login by username. The source's slot is taken first, then the username's, keyed by
    /// the username as it is looked up and before the lookup, so a username no account has is
    /// counted exactly like one that exists; the count alone decides whether the attempt is refused.
    /// An unknown username still costs one BCrypt verify, so the time taken does not tell it from a
    /// known one (#471). The row's lock is checked before the password, so a locked account cannot
    /// be used to test passwords. The password is trimmed, as registration trims it.
    /// </summary>
    public async Task<PasswordAttempt> CheckAsync(string username, string password, LoginSource source,
        CancellationToken token)
    {
        if (!await TakeSourceAsync(source, "Login"))
            return new PasswordAttempt(PasswordCheck.SourceRefused, source, null, 0, null);

        string normalised = UsernameBudget.Normalise(username);
        string usernameKey = UsernameBudget.KeyFor(normalised);
        long taken = await UsernameBudget.TakeAsync(Cache, Limits, usernameKey);
        if (UsernameBudget.Refuses(Limits, taken))
        {
            Logger.LogWarning("Login refused for a username past its failed-login limit");
            return new PasswordAttempt(PasswordCheck.UsernameRefused, source, usernameKey, taken, null);
        }

        string trimmed = password.Trim();
        Account? account = await Accounts.FindByUserNameAsync(normalised, token);
        // A name outside the username rule, as sent, is an unknown username (owner decision, #487
        // re-review), whatever row may hold its normalised form. The lookup above still runs and the
        // same dummy verify follows, so it is answered as fast, and as, an unknown one.
        if (account == null || !UsernameRule.IsValid(username))
        {
            _verifier.Verify(trimmed, BCryptPasswordVerifier.UnknownAccountHash);
            return new PasswordAttempt(PasswordCheck.UnknownUsername, source, usernameKey, taken, null);
        }

        return Check(account, trimmed, source, usernameKey, taken);
    }

    /// <summary>
    /// Re-authenticates a signed-in account before a sensitive action (#478, #483): the same
    /// budgets, lock check and verify as a login, so a wrong password here counts against the
    /// account exactly as a failed login does.
    /// </summary>
    public async Task<PasswordAttempt> CheckAsync(Account account, string password, LoginSource source,
        CancellationToken token)
    {
        if (!await TakeSourceAsync(source, "Re-authentication"))
            return new PasswordAttempt(PasswordCheck.SourceRefused, source, null, 0, account);

        string usernameKey = UsernameBudget.KeyFor(account.Username);
        long taken = await UsernameBudget.TakeAsync(Cache, Limits, usernameKey);
        if (UsernameBudget.Refuses(Limits, taken))
        {
            Logger.LogWarning("Re-authentication for account {AccountId} refused: too many failed logins", account.Id);
            return new PasswordAttempt(PasswordCheck.UsernameRefused, source, usernameKey, taken, account);
        }

        return Check(account, password.Trim(), source, usernameKey, taken);
    }

    private PasswordAttempt Check(Account account, string password, LoginSource source, string usernameKey, long taken)
    {
        if (account.IsLockedAt(DateTime.UtcNow))
        {
            // Pay for one verify against the fixed hash (#478 review), as an unknown username does:
            // a locked row whose budget hold is gone (expired, or lost by Redis) must not answer
            // measurably faster. The account's own hash is never checked while it is locked.
            _verifier.Verify(password, BCryptPasswordVerifier.UnknownAccountHash);
            return new PasswordAttempt(PasswordCheck.Locked, source, usernameKey, taken, account);
        }

        bool right = _verifier.Verify(password, Encoding.UTF8.GetString(account.Verifier));
        return new PasswordAttempt(right ? PasswordCheck.Correct : PasswordCheck.WrongPassword, source, usernameKey,
            taken, account);
    }
}

/// <summary>The MFA step of a login: a TOTP code against the hash the password step issued.</summary>
public sealed class MfaLoginPolicy : LoginPolicy
{
    private readonly IMFAService _mfa;
    private readonly IMFAHashService _hashes;

    public MfaLoginPolicy(IAccountRepository accounts, IReplicatedCache cache, ILoginLimits limits, IMFAService mfa,
        IMFAHashService hashes, ILoggerFactory loggerFactory)
        : base(accounts, cache, limits, loggerFactory.CreateLogger<MfaLoginPolicy>())
    {
        _mfa = mfa;
        _hashes = hashes;
    }

    /// <summary>
    /// Checks an MFA code. A code spends the source's budget like a password (#471), and each hash
    /// allows <see cref="ILoginLimits.MaxFailedMfaAttempts"/> codes, counted before the code is
    /// checked so parallel attempts cannot exceed it; the last failure deletes the hash, and the
    /// client has to log in with the password again. A code also spends the username's budget
    /// (#484), since a fresh password login makes a fresh hash. The row's lock is checked before the
    /// code. Only one caller can be told <see cref="MfaCodeCheck.Correct"/> for a hash:
    /// <see cref="IMFAService.VerifyMFAAsync"/> spends it (#478).
    /// </summary>
    public async Task<MfaCodeAttempt> CheckAsync(string hash, string code, LoginSource source, CancellationToken token)
    {
        if (!await TakeSourceAsync(source, "MFA verify"))
            return new MfaCodeAttempt(MfaCodeCheck.SourceRefused, source, null, 0, null);

        AccountId? hashAccountId = await _hashes.GetAccountIdAsync(hash);
        if (hashAccountId == null)
            return new MfaCodeAttempt(MfaCodeCheck.HashGone, source, null, 0, null);

        Account? account = await Accounts.FindByIdAsync(hashAccountId, false, token);
        if (account == null)
        {
            Logger.LogWarning("Account {AccountId} of an MFA hash was not found", hashAccountId);
            return new MfaCodeAttempt(MfaCodeCheck.AccountMissing, source, null, 0, null);
        }

        // The hash was issued by a password login at the version of the row that password matched
        // (#495). A password change, an MFA reset or an admin's MFA removal since then has moved
        // the account on, and the hash is gone: a right code on it logs nobody in. Checked before
        // an attempt is counted (#495 re-review): attempts count on the account's record, which a
        // newer login's hash may hold, and a stale presentation must not spend that login's tries.
        if (await _hashes.GetHashCredentialsVersionAsync(hash) != account.CredentialsVersion)
        {
            Logger.LogWarning("MFA hash for account {AccountId} predates a credentials change", account.Id);
            await _hashes.CleanupHash(hash);
            return new MfaCodeAttempt(MfaCodeCheck.HashGone, source, null, 0, null);
        }

        long attempts = await _hashes.RecordAttemptAsync(hashAccountId);

        // Fail closed on a hash that is gone: a reverse key that outlived the :mfa hash for a moment
        // (an expiry, or an admin removing MFA deletes them one at a time).
        if (attempts < 0)
            return new MfaCodeAttempt(MfaCodeCheck.HashGone, source, null, 0, null);

        if (attempts > Limits.MaxFailedMfaAttempts)
        {
            await _hashes.CleanupHash(hash);
            return new MfaCodeAttempt(MfaCodeCheck.HashSpent, source, null, 0, null);
        }

        string usernameKey = UsernameBudget.KeyFor(account.Username);
        long taken = await UsernameBudget.TakeAsync(Cache, Limits, usernameKey);
        if (UsernameBudget.Refuses(Limits, taken))
        {
            Logger.LogWarning("MFA verify for account {AccountId} refused: too many failed logins", account.Id);
            return new MfaCodeAttempt(MfaCodeCheck.UsernameRefused, source, usernameKey, taken, account);
        }

        // Failed logins inside the hash's two minutes can lock the account after its password step
        // passed (#471). Before the code check, as for a password.
        if (account.IsLockedAt(DateTime.UtcNow))
        {
            Logger.LogWarning("Account {AccountId} refused at MFA verify while locked", account.Id);
            return new MfaCodeAttempt(MfaCodeCheck.Locked, source, usernameKey, taken, account);
        }

        MFAVerifyResult result = await _mfa.VerifyMFAAsync(hash, code, token);
        if (!result.Success && result.Refusal != MfaCodeRefusal.WrongCode)
        {
            // A replayed code, or one that lost the hash to another verify (#478 review): refused,
            // but not a failed login. Its budget slots come back, nothing is written to the row, and
            // the hash is not deleted. A replay also gives back the attempt it counted on the hash
            // (#478 re-review); that is safe because only a right code gets here. A code that lost
            // the hash gives none back: the hash is gone, and the per-account key may already hold
            // the next login's hash.
            var refused = new MfaCodeAttempt(MfaCodeCheck.Replayed, source, usernameKey, taken, account);
            await GiveBackAsync(refused);
            if (result.Refusal == MfaCodeRefusal.Replayed)
                await _hashes.GiveBackAttemptAsync(account.Id);
            return refused;
        }

        if (!result.Success || result.AccountId != account.Id)
        {
            if (attempts >= Limits.MaxFailedMfaAttempts)
            {
                Logger.LogWarning("MFA hash for account {AccountId} spent after {Attempts} wrong codes", hashAccountId, attempts);
                await _hashes.CleanupHash(hash);
            }

            return new MfaCodeAttempt(MfaCodeCheck.WrongCode, source, usernameKey, taken, account);
        }

        return new MfaCodeAttempt(MfaCodeCheck.Correct, source, usernameKey, taken, account);
    }
}
