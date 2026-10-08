using System.Security.Cryptography;
using System.Text;
using Avalon.Api.Hosting.Config;
using Avalon.Api.Identity.Config;
using Avalon.Infrastructure.GameAuth;

namespace Avalon.Api.Identity.Authentication;

/// <summary>
/// The key <see cref="GameAuthCryptography"/> derives its proof, replay receipt and Steam OpenID state keys from (#801,
/// design D4.3): <see cref="GameAuthConfig.HostKey"/>. A deployment from before #801 gives it the value of the HS256 key
/// access tokens were signed with then, from which these keys were derived, so what was protected before stays
/// readable; that key no longer stands in for it. Required where identity runs: a missing or unusable one stops identity
/// at startup, naming the setting (<see cref="IdentityStartupCheck"/>). Like every secret key the API reads as text since
/// #482, it is never committed: it comes from user-secrets in development and from the environment everywhere else.
/// </summary>
public sealed class GameAuthHostKey
{
    /// <summary>The key is an HMAC-SHA256 key, which needs at least as many bytes as its 256-bit output.</summary>
    public const int MinimumBytes = 32;

    // SHA-256 of keys that have been public and must never be used again: the HS256 signing key committed to
    // appsettings.json until #482, whose value a deployment of that time carries over into the host key. Only the hash
    // is kept, so the key itself stays out of the code.
    public static readonly IReadOnlyList<string> BlockedKeyHashes =
    [
        "99e2138407b7f8aa4be292593a9432ad73a5b869d19a22d34cc3b63e1552c645",
    ];

    private GameAuthHostKey(byte[] bytes) => Bytes = bytes;

    public byte[] Bytes { get; }

    /// <summary>
    /// The UTF-8 bytes of the host key <paramref name="gameAuth"/> holds, or an <see cref="InvalidOperationException"/>
    /// naming <see cref="GameAuthConfig.HostKeySetting"/> when it is missing, has leading or trailing whitespace, is
    /// shorter than <see cref="MinimumBytes"/> bytes in UTF-8, or is one of <see cref="BlockedKeyHashes"/>.
    /// </summary>
    public static GameAuthHostKey From(GameAuthConfig? gameAuth) => From(gameAuth, BlockedKeyHashes);

    /// <summary>
    /// <see cref="From(GameAuthConfig?)"/> against <paramref name="blockedHashes"/> (lower-case hex SHA-256 of the UTF-8
    /// key) instead of <see cref="BlockedKeyHashes"/>, so tests can block a made-up key. Public rather than internal:
    /// Avalon.Api.Identity is strong-named and the test assembly is not, so InternalsVisibleTo is not available (the
    /// repository's convention for such seams), and a caller passing its own list blocks nothing it could not already
    /// choose not to configure.
    /// </summary>
    public static GameAuthHostKey From(GameAuthConfig? gameAuth, IReadOnlyCollection<string> blockedHashes)
    {
        const string Setting = GameAuthConfig.HostKeySetting;
        string? key = gameAuth?.HostKey;
        string howToSet =
            $"Set {Setting} to a random value of at least {MinimumBytes} bytes: in development run " +
            $"`dotnet user-secrets set \"{Setting}\" \"<key>\" --project src/Server/Avalon.Api`, " +
            $"elsewhere set the environment variable {Setting.Replace(":", "__", StringComparison.Ordinal)}. " +
            "See docs/development-setup.md, \"REST API signing key\".";

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                $"{Setting} is not set, and identity needs it: the HS256 key ({TokenValidationConfig.IssuerSigningKeySetting}) " +
                $"no longer stands in for it (#801). A deployment from before #801 gives it that key's value. {howToSet}");
        }

        // Refused rather than trimmed: a newline from a key file would otherwise key with bytes
        // nobody meant, and would slip a blocked key past the hash check below.
        if (!string.Equals(key, key.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{Setting} has leading or trailing whitespace, often a newline from the file it was read from. " +
                $"Remove it. {howToSet}");
        }

        byte[] bytes = Encoding.UTF8.GetBytes(key);
        if (bytes.Length < MinimumBytes)
        {
            throw new InvalidOperationException(
                $"{Setting} is {bytes.Length} bytes; it needs at least {MinimumBytes}. {howToSet}");
        }

        string hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (blockedHashes.Contains(hash, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"{Setting} is a key that was committed to the repository and is public; anyone could forge " +
                $"what it protects. Generate a new one. {howToSet}");
        }

        return new GameAuthHostKey(bytes);
    }
}
