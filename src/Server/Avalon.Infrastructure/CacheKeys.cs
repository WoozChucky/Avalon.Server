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
    /// Game-client login and MFA-code attempts from one source, across every account it tried (#471).
    /// The source is an IPv4 address, or an IPv6 /64 prefix written <c>prefix::/64</c>.
    /// Value: counter written with INCR before each attempt; a correct password or code gives its own
    /// slot back with a floored DECR. Expires at the end of a fixed window from the first attempt
    /// (<c>Application:FailedLoginSourceWindowMinutes</c>, default 15 minutes).
    /// </summary>
    public static string AuthSourceFailedLogins(string source) => $"auth:source:{source}:failedLogins";

    /// <summary>
    /// Game-client login and MFA-code attempts at one username, from every source (#484). It decides
    /// the account lock: an attempt past <c>Application:MaxFailedLoginAttempts</c> is refused as
    /// LOCKED before the username is looked up, whether or not an account has it. The segment is the
    /// lowercase hex SHA-256 of the username trimmed and upper-cased (the form it is looked up by),
    /// so the key's length does not depend on what a client sends.
    /// Value: counter written with INCR before each attempt; a correct password or code gives its own
    /// slot back with a floored DECR. Expires <c>Application:LockoutDurationMinutes</c> (default 15)
    /// after the first attempt, and the failure that reaches the limit restarts that expiry, so the
    /// refusal ends when the account row's lock does.
    /// </summary>
    public static string AuthUsernameFailedLogins(string usernameHash) => $"auth:username:{usernameHash}:failedLogins";

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
