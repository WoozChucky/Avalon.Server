namespace Avalon.Infrastructure;

/// <summary>
/// Centralizes all Redis cache key and channel strings used across the Avalon services.
/// Use the static methods for keys that include dynamic segments; use the constants for fixed channels.
/// </summary>
public static class CacheKeys
{
    /// <summary>
    /// An account id with a credentials version, as <c>{accountId}:{version}</c> (#495): the value
    /// stored under <see cref="WorldKey"/> (the version of the connection that selected the world)
    /// and under <see cref="MfaReverseHash"/> (the version of the login that issued the hash).
    /// </summary>
    public static string WorldKeyValue(long accountId, int credentialsVersion) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{accountId}:{credentialsVersion}");

    /// <summary>Reads a <see cref="WorldKeyValue"/>. False for anything else, a bare id included.</summary>
    public static bool TryParseWorldKeyValue(string? value, out long accountId, out int credentialsVersion)
    {
        accountId = 0;
        credentialsVersion = 0;
        if (value is null)
            return false;
        int colon = value.IndexOf(':', StringComparison.Ordinal);
        return colon > 0
               && long.TryParse(value.AsSpan(0, colon), System.Globalization.NumberStyles.None,
                   System.Globalization.CultureInfo.InvariantCulture, out accountId)
               && int.TryParse(value.AsSpan(colon + 1), System.Globalization.NumberStyles.None,
                   System.Globalization.CultureInfo.InvariantCulture, out credentialsVersion);
    }

    // ── Pub/Sub Channels (fixed) ──────────────────────────────────────────────

    /// <summary>
    /// Published, with the account id, whenever an account's sessions must end: a duplicate login,
    /// a password change, an MFA reset or removal, a ban, an email change, a refresh-token reuse.
    /// Subscribed by World servers to close the matching in-world connection, and by the Auth server
    /// to close the account's logged-in auth connections (#495).
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
    /// Login and MFA-code attempts from one source, across every account it tried (#471), over the game
    /// client's TCP login and the REST API alike (#478).
    /// The source is an IPv4 address, or an IPv6 /64 prefix written <c>prefix::/64</c>.
    /// Value: counter written with INCR before each attempt; a correct password or code gives its own
    /// slot back with a floored DECR. Expires at the end of a fixed window from the first attempt
    /// (<c>Application:FailedLoginSourceWindowMinutes</c>, default 15 minutes).
    /// </summary>
    public static string AuthSourceFailedLogins(string source) => $"auth:source:{source}:failedLogins";

    /// <summary>
    /// Accounts created by one source (same source form as <see cref="AuthSourceFailedLogins"/>) in the
    /// current window (#495 review). <c>INCR</c> before the insert, the expiry set by the first; given
    /// back only when the registration does not create its account.
    /// </summary>
    public static string AuthSourceAccountsCreated(string source) => $"auth:source:{source}:accountsCreated";

    /// <summary>
    /// Login and MFA-code attempts at one username, from every source and over both the TCP login and the
    /// REST API (#484, #478). It decides
    /// the account lock: an attempt past <c>Application:MaxFailedLoginAttempts</c> is refused as
    /// LOCKED before the username is looked up, whether or not an account has it. The segment is the
    /// lowercase hex SHA-256 of the username trimmed and upper-cased (the form it is looked up by),
    /// so the key's length does not depend on what a client sends.
    /// Value: counter written with INCR before each attempt. Expires
    /// <c>Application:LockoutDurationMinutes</c> (default 15) after the first attempt. The failure
    /// that reaches the limit holds it in one script (raised to at least the limit + 1 and SET with
    /// a fresh expiry, recreated if it had expired), so the refusal outlasts the account row's lock.
    /// A correct password that issues an MFA hash gives its own slot back in one script that
    /// decrements only while 0 &lt; value &lt;= the limit, so it never lowers a held value. A
    /// completed login deletes the key in one script, only while its value is below the held value
    /// (the limit + 1), so a reset never deletes a hold; the key still ends with its expiry.
    /// </summary>
    public static string AuthUsernameFailedLogins(string usernameHash) => $"auth:username:{usernameHash}:failedLogins";

    // ── Hash Keys ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Redis hash that holds MFA state for an account during the two-factor login flow.
    /// Fields: <c>hash</c>, <c>expiry</c>, <c>accountId</c>, and <c>attempts</c>: codes tried against it,
    /// counted with HINCRBY only while the hash exists; a replayed right code gives its attempt back
    /// with a script that decrements only while the hash exists and the field is above zero.
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
