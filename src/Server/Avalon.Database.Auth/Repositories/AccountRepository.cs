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
    /// Records an API login: last IP and login time, failed count cleared and an expired lock lifted.
    /// Returns false without writing when the account is locked at the time of the update, so a
    /// lock set after the caller read the account survives. Does not change game-session state.
    /// </summary>
    Task<bool> TryRecordApiLoginAsync(AccountId id, string lastIp, DateTime now, CancellationToken cancellationToken = default);
}

/// <summary>The account's failed-login count and lock state after a failure was recorded.</summary>
public readonly record struct FailedLoginResult(int FailedLogins, bool Locked);

public class AccountRepository(IDbContextFactory<AuthDbContext> contextFactory)
    : EntityFrameworkRepository<Account, AccountId, AuthDbContext>(contextFactory), IAccountRepository
{
    /// <summary>
    /// Holds the account row until the caller's transaction ends, under current game authority. The password lock is not
    /// asked (#882): it guards the password steps only, so a guesser who locks the account can neither refuse a join
    /// ticket nor end a session in progress.
    /// </summary>
    internal static async Task<bool> HoldGameAuthorityAsync(AuthDbContext db, AccountId accountId,
        int credentialsVersion, long sessionEpoch, CancellationToken cancellationToken) =>
        await db.Accounts.Where(a => a.Id == accountId && a.CredentialsVersion == credentialsVersion &&
            a.SessionEpoch == sessionEpoch && a.Status == AccountStatus.Active && a.GameplayConsolidationId == null &&
            (a.AccessLevel & AccountAccessLevel.Player) == AccountAccessLevel.Player)
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.SessionEpoch, a => a.SessionEpoch), cancellationToken) == 1;

    public async Task<Account?> FindByUserNameAsync(string userName, CancellationToken cancellationToken = default)
    {
        await using AuthDbContext context = await CreateContextAsync(cancellationToken);

        return await context.Accounts
            .AsNoTracking()
            .Where(x => x.Username == userName)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<FailedLoginResult> RecordFailedLoginAsync(AccountId id, string attemptIp, DateTime now,
        DateTime? lockUntil, CancellationToken cancellationToken = default)
    {
        await using AuthDbContext context = await CreateContextAsync(cancellationToken);
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

        FailedLoginResult state = await context.Accounts
            .AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new FailedLoginResult(a.FailedLogins, a.Locked))
            .FirstOrDefaultAsync(cancellationToken);
        return state;
    }

    public async Task<bool> TryRecordApiLoginAsync(AccountId id, string lastIp, DateTime now,
        CancellationToken cancellationToken = default)
    {
        await using AuthDbContext context = await CreateContextAsync(cancellationToken);

        int updated = await context.Accounts
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
    /// Inserts new accounts on a context the caller owns, so they commit with what the caller writes beside them in its
    /// transaction (a load-test run's licenses), and sets their ids. New rows only: no row that was read is written back.
    /// A username already taken fails the save, and the caller's transaction with it. Nothing else is checked here: the
    /// caller owns all validation of the rows (access level, the username rule, the email's form), as registration owns
    /// it for its own insert.
    /// </summary>
    public static async Task InsertManyAsync(AuthDbContext context, IReadOnlyList<Account> accounts,
        CancellationToken cancellationToken = default)
    {
        foreach (Account account in accounts)
            context.TrackForInsert(account);
        await context.SaveChangesAsync(cancellationToken);
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
    /// Sets the status by column, on a context the caller owns so the write joins its transaction (a ban revokes the
    /// account's tokens with it), and writes nothing else. A ban or a deactivation also raises <c>SessionEpoch</c> by
    /// one in the same statement (#882), as a consolidation does for the account it retires: every game context,
    /// launcher handoff, link proposal and game session the account holds is bound to the epoch it was issued at, so
    /// each is void from this commit on, and a later reactivation revives none of them. Returns the rows written: 0
    /// when no account has <paramref name="id"/>.
    /// </summary>
    public static Task<int> SetStatusAsync(AuthDbContext context, AccountId id, AccountStatus status,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Account> account = context.Accounts.Where(a => a.Id == id);
        return status == AccountStatus.Active
            ? account.ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, status), cancellationToken)
            : account.ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, status)
                .SetProperty(a => a.SessionEpoch, a => a.SessionEpoch + 1), cancellationToken);
    }

    /// <summary>Only an email-change confirmation may establish proof for the replacement address.</summary>
    public static Task<int> SetConfirmedEmailAsync(AuthDbContext context, AccountId id, string email, int expectedVersion,
        DateTime verifiedAt, CancellationToken cancellationToken = default) => context.Accounts
        .Where(a => a.Id == id && a.CredentialsVersion == expectedVersion)
        .ExecuteUpdateAsync(s => s.SetProperty(a => a.Email, email).SetProperty(a => a.EmailVerifiedAt, (DateTime?)verifiedAt)
            .SetProperty(a => a.CredentialsVersion, a => a.CredentialsVersion + 1), cancellationToken);

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

    /// <summary>
    /// Looks the email up in its stored form (#503): <paramref name="email"/> is normalised first, so
    /// <c>A@X.com</c> and <c>a@x.com</c> find the same account.
    /// </summary>
    public async Task<Account?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        await using AuthDbContext context = await CreateContextAsync(cancellationToken);
        string normalised = AccountEmail.Normalise(email);

        return await context.Accounts
            .AsNoTracking()
            .Where(x => x.Email == normalised)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
