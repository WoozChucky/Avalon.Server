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
    /// Marks a login on the account (online, <paramref name="lastIp"/>, last login time, failed count
    /// cleared, an expired lock lifted), but only while the account is not locked at
    /// <paramref name="now"/>. Returns <c>false</c>, writing nothing, when it is: a lock set since the
    /// account was read is never erased.
    /// </summary>
    Task<bool> TryRecordLoginAsync(AccountId id, string lastIp, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>
    /// The REST API's <see cref="TryRecordLoginAsync"/> (#478): the same write, on the same
    /// condition, minus <c>Online</c>, which is the game client's session flag and not the API's.
    /// </summary>
    Task<bool> TryRecordApiLoginAsync(AccountId id, string lastIp, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>Sets the email, and writes nothing else. False when no account has <paramref name="id"/>.</summary>
    Task<bool> SetEmailAsync(AccountId id, string email, CancellationToken cancellationToken = default);

    /// <summary>Sets the access level, and writes nothing else. False when no account has <paramref name="id"/>.</summary>
    Task<bool> SetAccessLevelAsync(AccountId id, AccountAccessLevel accessLevel, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets <c>Online = false</c>, adds <paramref name="sessionSeconds"/> to <c>TotalTime</c>, and
    /// writes nothing else (#484): a lock, a ban or a failed-login count written since the account
    /// was read is kept.
    /// </summary>
    Task MarkOfflineAsync(AccountId id, long sessionSeconds = 0, CancellationToken cancellationToken = default);

    /// <summary>Sets <c>Online = false</c> on every account, in one statement, and writes nothing else.</summary>
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

    public async Task<bool> TryRecordLoginAsync(AccountId id, string lastIp, DateTime now,
        CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);

        var updated = await context.Accounts
            .Where(a => a.Id == id && (!a.Locked || (a.LockedUntil != null && a.LockedUntil <= now)))
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Online, true)
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

    public async Task<bool> SetEmailAsync(AccountId id, string email, CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);

        return await context.Accounts
            .Where(a => a.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Email, email), cancellationToken) == 1;
    }

    public async Task<bool> SetAccessLevelAsync(AccountId id, AccountAccessLevel accessLevel,
        CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);

        return await context.Accounts
            .Where(a => a.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.AccessLevel, accessLevel), cancellationToken) == 1;
    }

    /// <summary>
    /// Sets the password's salt and verifier on a context the caller owns, so the write joins that
    /// context's transaction (a password change revokes the account's tokens with it), and writes
    /// nothing else. Returns the rows written: 0 when no account has <paramref name="id"/>.
    /// </summary>
    public static Task<int> SetPasswordAsync(AuthDbContext context, AccountId id, byte[] salt, byte[] verifier,
        CancellationToken cancellationToken = default)
    {
        return context.Accounts
            .Where(a => a.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Salt, salt)
                .SetProperty(a => a.Verifier, verifier), cancellationToken);
    }

    public async Task MarkOfflineAsync(AccountId id, long sessionSeconds = 0, CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);

        await context.Accounts
            .Where(a => a.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Online, false)
                .SetProperty(a => a.TotalTime, a => a.TotalTime + sessionSeconds), cancellationToken);
    }

    public async Task MarkAllOfflineAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);

        await context.Accounts
            .Where(a => a.Online)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Online, false), cancellationToken);
    }

    public async Task SetSessionKeyAsync(AccountId id, byte[] sessionKey, CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);

        await context.Accounts
            .Where(a => a.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.SessionKey, sessionKey), cancellationToken);
    }

    public async Task<Account?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);

        return await context.Accounts
            .AsNoTracking()
            .Where(x => x.Email == email)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
