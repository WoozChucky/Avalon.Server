namespace Avalon.Infrastructure;

/// <summary>
/// Centralizes all Redis cache key and channel strings used across the Avalon services.
/// Use the static methods for keys that include dynamic segments; use the constants for fixed channels.
/// </summary>
public static class CacheKeys
{
    // ── Pub/Sub Channels (fixed) ──────────────────────────────────────────────

    /// <summary>
    /// Published by the Auth server when a duplicate login triggers a forced disconnect.
    /// Subscribed by World servers to close the matching in-world connection.
    /// </summary>
    public const string WorldAccountsDisconnectChannel = "world:accounts:disconnect";

    /// <summary>
    /// Published by the Auth server when an account successfully authenticates.
    /// </summary>
    public const string AuthAccountsOnlineChannel = "auth:accounts:online";

    /// <summary>
    /// Glob pattern used to scan all active MFA hash entries. Passed to the Redis KEYS command.
    /// </summary>
    public const string AccountMfaGlobPattern = "auth:account:*:mfa";

    // ── Pub/Sub Channels (dynamic) ────────────────────────────────────────────

    /// <summary>
    /// Published by the Auth server when an account selects a world to enter.
    /// Message format: <c>account:{accountId}:worldKey:{worldKeyBase64}</c>.
    /// </summary>
    public static string WorldSelectChannel(ushort worldId) => $"world:{worldId}:select";

    // ── String Keys ───────────────────────────────────────────────────────────

    /// <summary>
    /// One-time authentication token that maps a world entry key to the account ID attempting to join.
    /// Value: account ID string. Expires after 5 minutes.
    /// </summary>
    public static string WorldKey(ushort worldId, string worldKeyBase64) => $"world:{worldId}:keys:{worldKeyBase64}";

    /// <summary>
    /// Mutex key that prevents an account from holding more than one active world session concurrently.
    /// Written with SETNX; removed on successful hand-off. Value: "1". Expires after 5 minutes.
    /// </summary>
    public static string AccountInWorld(long accountId) => $"account:{accountId}:inWorld";

    /// <summary>
    /// Failed game-client logins from one remote address, across every account it tried (#471).
    /// Value: counter written with INCR. Expires at the end of a fixed window from the first failure
    /// (<c>Application:FailedLoginSourceWindowMinutes</c>, default 15 minutes).
    /// </summary>
    public static string AuthSourceFailedLogins(string remoteAddress) => $"auth:source:{remoteAddress}:failedLogins";

    // ── Hash Keys ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Redis hash that holds MFA state for an account during the two-factor login flow.
    /// Fields: <c>hash</c>, <c>expiry</c>, <c>accountId</c>.
    /// </summary>
    public static string AccountMfa(long accountId) => $"auth:account:{accountId}:mfa";

    /// <summary>
    /// Reverse-lookup key for the MFA login flow.
    /// Maps a generated hash value back to the account ID it was issued for.
    /// Value: account ID string. Same 2-minute TTL as the forward hash entry.
    /// </summary>
    public static string MfaReverseHash(string hash) => $"auth:mfa:hash:{hash}";

    // ── Presence (live player observability) ──────────────────────────────────

    /// <summary>
    /// Live presence snapshot for one world, written by that world server at ~1 Hz.
    /// Value: JSON <c>WorldPresenceSnapshot</c>. Expires after <see cref="PresenceTtl"/>,
    /// so a dead world server's players disappear rather than going stale.
    /// </summary>
    public static string WorldPresence(ushort worldId) => $"world:{worldId}:presence";

    /// <summary>
    /// Reverse index: which world (and instance) currently holds a character.
    /// Value: JSON <c>CharacterPresenceIndex</c>. Lets the Api find a player without
    /// scanning every world snapshot. Same TTL as the snapshot it points at.
    /// </summary>
    public static string CharacterPresenceIndex(uint characterId) => $"presence:character:{characterId}";

    /// <summary>
    /// Lifetime of every presence key. Must stay comfortably above the snapshot write
    /// interval so a single slow tick does not blank the view.
    /// </summary>
    public static readonly TimeSpan PresenceTtl = TimeSpan.FromSeconds(5);
}
