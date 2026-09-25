using System.Security.Cryptography;
using System.Text;
using Avalon.Infrastructure;
using Avalon.Server.Auth.Configuration;

namespace Avalon.Server.Auth.Services;

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
    public static Task<long> TakeAsync(IReplicatedCache cache, AuthConfiguration config, string key) =>
        AttemptBudget.TakeAsync(cache, key, Window(config));

    /// <summary>Past the limit: refused as LOCKED before any work.</summary>
    public static bool Refuses(AuthConfiguration config, long taken) => taken > config.MaxFailedLoginAttempts;

    /// <summary>The attempt in the last slot: its failure locks the account.</summary>
    public static bool Locks(AuthConfiguration config, long taken) => taken >= config.MaxFailedLoginAttempts;

    /// <summary>
    /// Holds the lock: in one atomic step, raises the count to at least the limit and restarts the
    /// window from the failure that set the lock, recreating the key if it expired after this
    /// attempt took its slot. Call it after computing the row's <c>LockedUntil</c>, so the row's lock
    /// always ends first and the refusal lasts at least as long. Done for a username no account has
    /// too, so the two look the same (#484 review: a key that expired between the take and a plain
    /// EXPIRE left an unknown username unlocked while a known one's row stayed locked).
    /// </summary>
    public static Task HoldLockAsync(IReplicatedCache cache, AuthConfiguration config, string key) =>
        cache.HoldCounterAtLeastAsync(key, config.MaxFailedLoginAttempts, Window(config));

    /// <inheritdoc cref="AttemptBudget.GiveBackAsync"/>
    public static Task GiveBackAsync(IReplicatedCache cache, string key) => AttemptBudget.GiveBackAsync(cache, key);

    /// <summary>
    /// Clears the username's count once a login has fully completed (owner decision on #484): the
    /// login is recorded, and for an MFA account the code has been accepted too. Never at the
    /// password step of an MFA account, since every password login makes a fresh MFA hash and a
    /// reset there would hand out a fresh set of code guesses each time.
    /// </summary>
    public static Task ResetAsync(IReplicatedCache cache, string key) => cache.RemoveAsync(key);

    private static TimeSpan Window(AuthConfiguration config) => TimeSpan.FromMinutes(config.LockoutDurationMinutes);
}
