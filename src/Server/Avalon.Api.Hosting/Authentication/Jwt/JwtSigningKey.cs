using System.Security.Cryptography;
using System.Text;
using Avalon.Api.Hosting.Config;
using Microsoft.IdentityModel.Tokens;

namespace Avalon.Api.Hosting.Authentication.Jwt;

/// <summary>
/// The HS256 key access tokens were signed with before #801, <see cref="TokenValidationConfig.IssuerSigningKey"/>,
/// accepted beside the ES256 keys (<see cref="JwtKeys"/>) while it is set; and the rules every secret key the API reads
/// as text follows, this one's since #482 and the game-auth host key's since #801. No key is ever committed: each comes
/// from user-secrets in development and from the environment everywhere else.
/// </summary>
public static class JwtSigningKey
{
    public const string SettingName = TokenValidationConfig.Section + ":IssuerSigningKey";

    /// <summary>HMAC-SHA256 needs a key at least as long as its 256-bit output.</summary>
    public const int MinimumBytes = 32;

    // SHA-256 of keys that have been public and must never sign again: the one committed to
    // appsettings.json until #482. Only the hash is kept, so the key itself stays out of the code.
    public static readonly IReadOnlyList<string> BlockedKeyHashes =
    [
        "99e2138407b7f8aa4be292593a9432ad73a5b869d19a22d34cc3b63e1552c645",
    ];

    /// <summary>
    /// The HS256 key, or null when <see cref="TokenValidationConfig.IssuerSigningKey"/> is unset (an empty value counts
    /// as unset). A key that breaks <see cref="SecretBytes"/>'s rules stops startup, naming the setting.
    /// </summary>
    public static SymmetricSecurityKey? Create(TokenValidationConfig? config) => Create(config, BlockedKeyHashes);

    /// <summary>
    /// <see cref="Create(TokenValidationConfig?)"/> against <paramref name="blockedHashes"/> (lower-case hex SHA-256 of
    /// the UTF-8 key) instead of <see cref="BlockedKeyHashes"/>, so tests can block a made-up key. Public rather than
    /// internal: Avalon.Api.Hosting is strong-named and the test assembly is not, so InternalsVisibleTo is not available
    /// (the repository's convention for such seams), and a caller passing its own list blocks nothing it could not
    /// already choose not to configure.
    /// </summary>
    public static SymmetricSecurityKey? Create(TokenValidationConfig? config, IReadOnlyCollection<string> blockedHashes)
    {
        string? key = config?.IssuerSigningKey;
        return string.IsNullOrEmpty(key) ? null : new SymmetricSecurityKey(SecretBytes(key, SettingName, blockedHashes));
    }

    /// <summary>
    /// The UTF-8 bytes of <paramref name="key"/>, the value of the secret key setting <paramref name="settingName"/>,
    /// or an <see cref="InvalidOperationException"/> naming the setting when the key is missing, has leading or trailing
    /// whitespace, is shorter than <see cref="MinimumBytes"/> bytes in UTF-8, or is one of <paramref name="blockedHashes"/>.
    /// </summary>
    public static byte[] SecretBytes(string? key, string settingName, IReadOnlyCollection<string> blockedHashes)
    {
        string howToSet =
            $"Set {settingName} to a random value of at least {MinimumBytes} bytes: in development run " +
            $"`dotnet user-secrets set \"{settingName}\" \"<key>\" --project src/Server/Avalon.Api`, " +
            $"elsewhere set the environment variable {settingName.Replace(":", "__", StringComparison.Ordinal)}. " +
            "See docs/development-setup.md, \"REST API signing key\".";

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException($"{settingName} is not set. {howToSet}");
        }

        // Refused rather than trimmed: a newline from a key file would otherwise key with bytes
        // nobody meant, and would slip a blocked key past the hash check below.
        if (!string.Equals(key, key.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{settingName} has leading or trailing whitespace, often a newline from the file it was read from. " +
                $"Remove it. {howToSet}");
        }

        byte[] bytes = Encoding.UTF8.GetBytes(key);
        if (bytes.Length < MinimumBytes)
        {
            throw new InvalidOperationException(
                $"{settingName} is {bytes.Length} bytes; it needs at least {MinimumBytes}. {howToSet}");
        }

        string hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (blockedHashes.Contains(hash, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"{settingName} is a key that was committed to the repository and is public; anyone could forge " +
                $"tokens with it. Generate a new one. {howToSet}");
        }

        return bytes;
    }
}
