using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Avalon.Infrastructure;

namespace Avalon.Infrastructure.Login;

/// <summary>
/// The failed-attempt budget of one username, counted from every source (#484) and shared by the
/// password step and the MFA step. It is the account lock. The slot is taken before the username is
/// looked up, the same way for a username no account has as for one that exists, and the count it
/// returns, not the account row as read, decides whether the attempt is refused and whether its
/// failure locks the row. Its slots work as <see cref="AttemptBudget"/> describes.
/// </summary>
public static class UsernameBudget
{
    /// <summary>The form a username is looked up by: trimmed and upper-cased.</summary>
    public static string Normalise(string username) => username.Trim().ToUpperInvariant();

    public static string KeyFor(string username)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(Normalise(username)));
        return CacheKeys.AuthUsernameFailedLogins(Convert.ToHexStringLower(digest));
    }

    /// <summary>Takes a slot for this attempt and returns its place in the window.</summary>
    public static Task<long> TakeAsync(IReplicatedCache cache, ILoginLimits config, string key) =>
        AttemptBudget.TakeAsync(cache, key, Window(config));

    /// <summary>Past the limit: refused as LOCKED before any work.</summary>
    public static bool Refuses(ILoginLimits config, long taken) => taken > config.MaxFailedLoginAttempts;

    /// <summary>The attempt in the last slot: its failure locks the account.</summary>
    public static bool Locks(ILoginLimits config, long taken) => taken >= config.MaxFailedLoginAttempts;

    /// <summary>
    /// Holds the lock: in one atomic step, raises the count to at least <see cref="HeldValue"/> (one
    /// past the limit, so later attempts are refused and a reset can tell it apart) and restarts the
    /// window from the failure that set the lock, recreating the key if it expired after this
    /// attempt took its slot. Call it after computing the row's <c>LockedUntil</c>, so the row's lock
    /// always ends first and the refusal lasts at least as long. Done for a username no account has
    /// too, so the two look the same (#484 review: a key that expired between the take and a plain
    /// EXPIRE left an unknown username unlocked while a known one's row stayed locked).
    /// </summary>
    public static Task HoldLockAsync(IReplicatedCache cache, ILoginLimits config, string key) =>
        cache.HoldCounterAtLeastAsync(key, HeldValue(config), Window(config));

    /// <summary>
    /// Gives back the slot this attempt took, and no more, but never out of a held count: the
    /// decrement runs only while the count is above zero and at most the limit (#484 re-review).
    /// A plain floored DECR let a give-back at MFA_REQUIRED lower a hold from six to five, after
    /// which a completed login's reset deleted it, or two give-backs brought it to four and the next
    /// guess was verified again.
    /// </summary>
    public static Task GiveBackAsync(IReplicatedCache cache, ILoginLimits config, string key) =>
        cache.DecrementCounterIfAtMostAsync(key, config.MaxFailedLoginAttempts);

    /// <summary>
    /// Clears the username's count once a login has fully completed (owner decision on #484): the
    /// login is recorded, and for an MFA account the code has been accepted too. Never at the
    /// password step of an MFA account, since every password login makes a fresh MFA hash and a
    /// reset there would hand out a fresh set of code guesses each time. The key is deleted only
    /// while the failures before this login are below the limit, in one script, so a hold that a
    /// concurrent last-slot failure set (with the row lock it goes with) is not deleted by a login
    /// that completed just before it (#484 re-review); <see cref="GiveBackAsync"/> never lowers a
    /// hold, so nothing brings one back within reach. The count still includes the login's own
    /// slot, so that is a count below <see cref="HeldValue"/>: four typos and a completed login
    /// leave five, and the key goes; a held key is at least six, and stays until it expires. Known
    /// edge case: an attempt refused past the budget that lands between this login's take and its
    /// reset also raises the count to six, so the key stays and the player is refused as LOCKED
    /// until the window ends, with no row lock. It fails closed and needs a race of milliseconds.
    /// </summary>
    public static Task ResetAsync(IReplicatedCache cache, ILoginLimits config, string key) =>
        cache.RemoveCounterIfBelowAsync(key, HeldValue(config));

    /// <summary>
    /// The value a hold raises the count to: one past the limit, which only a hold (or attempts
    /// already refused) can reach, so the reset can tell a held key from a count of failures.
    /// </summary>
    public static long HeldValue(ILoginLimits config) => config.MaxFailedLoginAttempts + 1L;

    /// <summary>
    /// After a failure has been answered: holds the budget when the failure is in the last slot,
    /// and runs <paramref name="recordRow"/> (null for a username no account has) with the row's
    /// lock end, taken before the hold so the row's lock always ends first. The row is written
    /// whatever the hold does; a hold error is logged first and rethrown after the write, so a
    /// failing write cannot hide it. A lock write never takes <paramref name="token"/>: a closing
    /// connection must not skip the lock (#484 re-review).
    /// </summary>
    public static async Task RecordFailureAsync(IReplicatedCache cache, ILoginLimits config, ILogger logger,
        string key, long taken, Func<DateTime, DateTime?, CancellationToken, Task>? recordRow, CancellationToken token)
    {
        bool locks = Locks(config, taken);
        DateTime now = DateTime.UtcNow;
        DateTime? lockUntil = locks ? now.AddMinutes(config.LockoutDurationMinutes) : null;

        Exception? holdError = null;
        if (locks)
        {
            try
            {
                await HoldLockAsync(cache, config, key);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not hold the failed-login budget of a locked username");
                holdError = e;
            }
        }

        if (recordRow != null)
        {
            await recordRow(now, lockUntil, lockUntil != null ? CancellationToken.None : token);
        }

        if (holdError != null)
        {
            ExceptionDispatchInfo.Throw(holdError);
        }
    }

    private static TimeSpan Window(ILoginLimits config) => TimeSpan.FromMinutes(config.LockoutDurationMinutes);
}
