using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Auth.Repositories;

public interface IAccountRepository : IRepository<Account, AccountId>
{
    Task<Account?> FindByUserNameAsync(string userName, CancellationToken cancellationToken = default);
    Task<Account?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records one failed login in SQL, so concurrent failures cannot overwrite one another's count.
    /// A lock that has expired at <paramref name="now"/> is lifted first and its count restarted.
    /// When <paramref name="lockUntil"/> is given, the account is locked until then: the caller
    /// decides that from the username's failure budget (#484), not from the stored count. An account
    /// already locked keeps its lock and its end.
    /// </summary>
    Task<FailedLoginResult> RecordFailedLoginAsync(AccountId id, string attemptIp, DateTime now, DateTime? lockUntil,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a login on the account (online, owned by <paramref name="sessionId"/>, the auth-server
    /// connection logging in; <paramref name="lastIp"/>, last login time, failed count cleared, an
    /// expired lock lifted), but only while the account is not locked at <paramref name="now"/>.
    /// Returns <c>false</c>, writing nothing, when it is: a lock set since the account was read is
    /// never erased.
    /// </summary>
    Task<bool> TryRecordLoginAsync(AccountId id, string lastIp, DateTime now, Guid sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The REST API's <see cref="TryRecordLoginAsync"/> (#478): the same write, on the same
    /// condition, minus <c>Online</c>, which is the game client's session flag and not the API's.
    /// </summary>
    Task<bool> TryRecordApiLoginAsync(AccountId id, string lastIp, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets <c>Online = false</c> (and clears <c>OnlineSessionId</c>), but only while the account's
    /// online session is <paramref name="sessionId"/> (#487): a session that is not the one online
    /// leaves the flag to the one that is. Adds <paramref name="sessionSeconds"/> to
    /// <c>TotalTime</c> either way, and writes nothing else (#484): a lock, a ban or a failed-login
    /// count written since the account was read is kept.
    /// </summary>
    Task MarkOfflineAsync(AccountId id, Guid? sessionId, long sessionSeconds = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets <c>Online = false</c> and clears <c>OnlineSessionId</c> on every account, in one
    /// statement, and writes nothing else. Run at auth-server start-up: it assumes this server is
    /// the only one, so every session it did not start is gone (#487).
    /// </summary>
    Task MarkAllOfflineAsync(CancellationToken cancellationToken = default);

    /// <summary>Stores the account's world session key, and writes nothing else.</summary>
    Task SetSessionKeyAsync(AccountId id, byte[] sessionKey, CancellationToken cancellationToken = default);
}

/// <summary>The account's failed-login count and lock state after a failure was recorded.</summary>
public readonly record struct FailedLoginResult(int FailedLogins, bool Locked);

public class AccountRepository(IDbContextFactory<AuthDbContext> contextFactory)
    : EntityFrameworkRepository<Account, AccountId, AuthDbContext>(contextFactory), IAccountRepository
{
    public async Task<Account?> FindByUserNameAsync(string userName, CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);

        return await context.Accounts
            .AsNoTracking()
            .Where(x => x.Username == userName)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<FailedLoginResult> RecordFailedLoginAsync(AccountId id, string attemptIp, DateTime now,
        DateTime? lockUntil, CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);
        bool lockNow = lockUntil != null;

        // An expired lock no longer counts: lift it and start again, so the failure below is the first.
        await context.Accounts
            .Where(a => a.Id == id && a.Locked && a.LockedUntil != null && a.LockedUntil <= now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Locked, false)
                .SetProperty(a => a.LockedUntil, (DateTime?)null)
                .SetProperty(a => a.FailedLogins, 0), cancellationToken);

        // One statement: every right-hand side reads the row as it was before this update, so a
        // concurrent failure is never lost and a lock already in place keeps its end.
        await context.Accounts
            .Where(a => a.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.LastAttemptIp, attemptIp)
                .SetProperty(a => a.FailedLogins, a => a.FailedLogins + 1)
                .SetProperty(a => a.LockedUntil, a => !a.Locked && lockNow ? lockUntil : a.LockedUntil)
                .SetProperty(a => a.Locked, a => a.Locked || lockNow),
                cancellationToken);

        var state = await context.Accounts
            .AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new FailedLoginResult(a.FailedLogins, a.Locked))
            .FirstOrDefaultAsync(cancellationToken);
        return state;
    }

    public async Task<bool> TryRecordLoginAsync(AccountId id, string lastIp, DateTime now, Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);

        var updated = await context.Accounts
            .Where(a => a.Id == id && (!a.Locked || (a.LockedUntil != null && a.LockedUntil <= now)))
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Online, true)
                .SetProperty(a => a.OnlineSessionId, (Guid?)sessionId)
                .SetProperty(a => a.LastIp, lastIp)
                .SetProperty(a => a.LastLogin, now)
                .SetProperty(a => a.FailedLogins, 0)
                .SetProperty(a => a.Locked, false)
                .SetProperty(a => a.LockedUntil, (DateTime?)null), cancellationToken);

        return updated == 1;
    }

    public async Task<bool> TryRecordApiLoginAsync(AccountId id, string lastIp, DateTime now,
        CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);

        var updated = await context.Accounts
            .Where(a => a.Id == id && (!a.Locked || (a.LockedUntil != null && a.LockedUntil <= now)))
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.LastIp, lastIp)
                .SetProperty(a => a.LastLogin, now)
                .SetProperty(a => a.FailedLogins, 0)
                .SetProperty(a => a.Locked, false)
                .SetProperty(a => a.LockedUntil, (DateTime?)null), cancellationToken);

        return updated == 1;
    }

    /// <summary>
    /// Sets the access level and raises <c>CredentialsVersion</c> by one (#504), in one statement,
    /// on a context the caller owns, so the write joins that context's transaction (a role change
    /// revokes the account's tokens with it), and writes nothing else. Run it first in that
    /// transaction: its row lock orders the change against a concurrent credential issue, as
    /// <see cref="BumpCredentialsVersionAsync"/> does. Returns the rows written: 0 when no account
    /// has <paramref name="id"/>.
    /// </summary>
    public static Task<int> SetAccessLevelAsync(AuthDbContext context, AccountId id, AccountAccessLevel accessLevel,
        CancellationToken cancellationToken = default)
    {
        return context.Accounts
            .Where(a => a.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.AccessLevel, accessLevel)
                .SetProperty(a => a.CredentialsVersion, a => a.CredentialsVersion + 1), cancellationToken);
    }

    /// <summary>
    /// Sets the email and raises <c>CredentialsVersion</c> by one (#503), in one statement, on a
    /// context the caller owns, and writes nothing else. <paramref name="email"/> must already be
    /// normalised (<see cref="AccountEmail.Normalise"/>): the unique index and the check constraint
    /// are on the stored form. A compare-and-set, like <see cref="SetPasswordAsync"/>: it writes only
    /// while the account is still at <paramref name="expectedVersion"/>, the version the current
    /// password was checked at when the change was started, so a password change, an MFA reset or
    /// a role change since then voids it. Returns the rows written: 0 when no account has
    /// <paramref name="id"/> or its version has moved.
    /// </summary>
    public static Task<int> SetEmailAsync(AuthDbContext context, AccountId id, string email, int expectedVersion,
        CancellationToken cancellationToken = default)
    {
        return context.Accounts
            .Where(a => a.Id == id && a.CredentialsVersion == expectedVersion)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Email, email)
                .SetProperty(a => a.CredentialsVersion, a => a.CredentialsVersion + 1), cancellationToken);
    }

    /// <summary>
    /// Sets the password's salt and verifier, and raises <c>CredentialsVersion</c> by one (#495),
    /// on a context the caller owns, so the write joins that context's transaction (a password
    /// change revokes the account's tokens with it), and writes nothing else. A compare-and-set:
    /// it writes only while the account is still at <paramref name="expectedVersion"/>, the
    /// version the current password was checked at, so of two changes that both proved the same
    /// password only the first lands. Returns the rows written: 0 when no account has
    /// <paramref name="id"/> or its version has moved.
    /// </summary>
    public static Task<int> SetPasswordAsync(AuthDbContext context, AccountId id, byte[] salt, byte[] verifier,
        int expectedVersion, CancellationToken cancellationToken = default)
    {
        return context.Accounts
            .Where(a => a.Id == id && a.CredentialsVersion == expectedVersion)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Salt, salt)
                .SetProperty(a => a.Verifier, verifier)
                .SetProperty(a => a.CredentialsVersion, a => a.CredentialsVersion + 1), cancellationToken);
    }

    /// <summary>
    /// Raises <c>CredentialsVersion</c> by one (#495) on a context the caller owns, so it commits
    /// with the change it records, and writes nothing else. Run it first in that transaction: the
    /// row lock it takes is what orders the change against a concurrent credential issue (see
    /// <see cref="HoldCredentialsVersionAsync"/>). Returns the rows written: 0 when no account has
    /// <paramref name="id"/>.
    /// </summary>
    public static Task<int> BumpCredentialsVersionAsync(AuthDbContext context, AccountId id,
        CancellationToken cancellationToken = default)
    {
        return context.Accounts
            .Where(a => a.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.CredentialsVersion, a => a.CredentialsVersion + 1),
                cancellationToken);
    }

    /// <summary>
    /// True when the account's <c>CredentialsVersion</c> is still <paramref name="version"/>
    /// (#495), and then holds the account row until the caller's transaction ends. Run it first in
    /// a transaction that issues a credential: a credentials change that commits first is seen
    /// here, and one that has not committed yet waits for this transaction and then revokes what
    /// it issued. False, holding nothing, when the version moved or no account has
    /// <paramref name="id"/>.
    /// </summary>
    public static async Task<bool> HoldCredentialsVersionAsync(AuthDbContext context, AccountId id,
        int version, CancellationToken cancellationToken = default)
    {
        // A write that changes nothing, so that it takes the row lock a read would not. In
        // Postgres, when a credentials change holds the row, this waits for it and then re-reads
        // the row it committed, so the condition sees the new version.
        return await context.Accounts
            .Where(a => a.Id == id && a.CredentialsVersion == version)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.CredentialsVersion, a => a.CredentialsVersion),
                cancellationToken) == 1;
    }

    public async Task MarkOfflineAsync(AccountId id, Guid? sessionId, long sessionSeconds = 0,
        CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);

        // One statement, whose conditions read the row as it was before it: the flag is cleared
        // only while this session is the one that set it, so a newer login's session survives a
        // stale close (#487), and the session's time is counted either way.
        await context.Accounts
            .Where(a => a.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Online, a => a.OnlineSessionId == sessionId ? false : a.Online)
                .SetProperty(a => a.OnlineSessionId,
                    a => a.OnlineSessionId == sessionId ? (Guid?)null : a.OnlineSessionId)
                .SetProperty(a => a.TotalTime, a => a.TotalTime + sessionSeconds), cancellationToken);
    }

    public async Task MarkAllOfflineAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);

        await context.Accounts
            .Where(a => a.Online || a.OnlineSessionId != null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Online, false)
                .SetProperty(a => a.OnlineSessionId, (Guid?)null), cancellationToken);
    }

    public async Task SetSessionKeyAsync(AccountId id, byte[] sessionKey, CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);

        await context.Accounts
            .Where(a => a.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.SessionKey, sessionKey), cancellationToken);
    }

    /// <summary>
    /// Looks the email up in its stored form (#503): <paramref name="email"/> is normalised first, so
    /// <c>A@X.com</c> and <c>a@x.com</c> find the same account.
    /// </summary>
    public async Task<Account?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);
        string normalised = AccountEmail.Normalise(email);

        return await context.Accounts
            .AsNoTracking()
            .Where(x => x.Email == normalised)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
