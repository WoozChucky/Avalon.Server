using Avalon.Infrastructure;
using Avalon.Server.Auth.Configuration;

namespace Avalon.Server.Auth.Services;

/// <summary>
/// The failed-attempt budget of one source address (#471), shared by the password step and the MFA
/// step. Every attempt takes a slot before any work is done, so parallel connections cannot all get
/// past a check made before the first of them is counted. An attempt that turns out correct gives
/// its own slot back; a failed one keeps it until the window ends.
/// </summary>
public static class SourceBudget
{
    /// <summary>Takes a slot for this attempt. Returns <c>false</c> when the source is over its limit.</summary>
    public static async Task<bool> TryTakeAsync(IReplicatedCache cache, AuthConfiguration config, string sourceKey)
    {
        long taken = await cache.IncrementAsync(sourceKey, TimeSpan.FromMinutes(config.FailedLoginSourceWindowMinutes));
        return taken <= config.MaxFailedLoginsPerSource;
    }

    /// <summary>
    /// Gives back the slot this attempt took, and no more: a success must not clear the failures
    /// others made, or one working account would let a source reset its own limit between guesses.
    /// </summary>
    public static Task GiveBackAsync(IReplicatedCache cache, string sourceKey) => cache.DecrementFloorAsync(sourceKey);

    public static string KeyFor(string remoteEndPoint) =>
        CacheKeys.AuthSourceFailedLogins(RemoteAddress.SourceOf(remoteEndPoint));
}
