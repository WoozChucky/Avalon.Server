namespace Avalon.Infrastructure.Login;

/// <summary>
/// The failed-attempt budget of one source address (#471), shared by the password step and the MFA
/// step. Its slots work as <see cref="AttemptBudget"/> describes.
/// </summary>
public static class SourceBudget
{
    /// <summary>Takes a slot for this attempt. Returns <c>false</c> when the source is over its limit.</summary>
    public static async Task<bool> TryTakeAsync(IReplicatedCache cache, ILoginLimits config, string sourceKey)
    {
        long taken = await AttemptBudget.TakeAsync(cache, sourceKey, TimeSpan.FromMinutes(config.FailedLoginSourceWindowMinutes));
        return taken <= config.MaxFailedLoginsPerSource;
    }

    /// <inheritdoc cref="AttemptBudget.GiveBackAsync"/>
    public static Task GiveBackAsync(IReplicatedCache cache, string sourceKey) => AttemptBudget.GiveBackAsync(cache, sourceKey);

    /// <summary>
    /// Takes a slot for this attempt from <paramref name="source"/>'s budget; an exempt source
    /// (<see cref="LoginSource.Exempt"/>) takes none and is never refused.
    /// </summary>
    public static Task<bool> TryTakeAsync(IReplicatedCache cache, ILoginLimits config, LoginSource source) =>
        source.Exempt ? Task.FromResult(true) : TryTakeAsync(cache, config, source.Key);

    /// <summary>Gives back the slot <see cref="TryTakeAsync(IReplicatedCache, ILoginLimits, LoginSource)"/> took, if any.</summary>
    public static Task GiveBackAsync(IReplicatedCache cache, LoginSource source) =>
        source.Exempt ? Task.CompletedTask : GiveBackAsync(cache, source.Key);

    public static string KeyFor(string remoteEndPoint) =>
        CacheKeys.AuthSourceFailedLogins(RemoteAddress.SourceOf(remoteEndPoint));

    /// <summary>The same key for an address the REST API has: one budget per source across both servers.</summary>
    public static string KeyFor(System.Net.IPAddress address) =>
        CacheKeys.AuthSourceFailedLogins(RemoteAddress.SourceOf(address));
}
