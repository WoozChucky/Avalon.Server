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
    /// The increment then locks the account until <paramref name="lockedUntil"/> when the stored
    /// count reaches <paramref name="maxFailedLogins"/>; an account already locked keeps its lock.
    /// </summary>
    Task<FailedLoginResult> RecordFailedLoginAsync(AccountId id, string attemptIp, DateTime now, int maxFailedLogins,
        DateTime lockedUntil, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a login on the account (online, <paramref name="lastIp"/>, last login time, failed count
    /// cleared, an expired lock lifted), but only while the account is not locked at
    /// <paramref name="now"/>. Returns <c>false</c>, writing nothing, when it is: a lock set since the
    /// account was read is never erased.
    /// </summary>
    Task<bool> TryRecordLoginAsync(AccountId id, string lastIp, DateTime now, CancellationToken cancellationToken = default);
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
        int maxFailedLogins, DateTime lockedUntil, CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);
        DateTime? until = lockedUntil;

        // An expired lock no longer counts: lift it and start again, so the failure below is the first.
        await context.Accounts
            .Where(a => a.Id == id && a.Locked && a.LockedUntil != null && a.LockedUntil <= now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Locked, false)
                .SetProperty(a => a.LockedUntil, (DateTime?)null)
                .SetProperty(a => a.FailedLogins, 0), cancellationToken);

        // One statement: every right-hand side reads the row as it was before this update, so the
        // increment and the lock decision agree, and a concurrent failure is never lost.
        await context.Accounts
            .Where(a => a.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.LastAttemptIp, attemptIp)
                .SetProperty(a => a.FailedLogins, a => a.FailedLogins + 1)
                .SetProperty(a => a.LockedUntil,
                    a => !a.Locked && a.FailedLogins + 1 >= maxFailedLogins ? until : a.LockedUntil)
                .SetProperty(a => a.Locked, a => a.Locked || a.FailedLogins + 1 >= maxFailedLogins),
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

    public async Task<Account?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);

        return await context.Accounts
            .AsNoTracking()
            .Where(x => x.Email == email)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
