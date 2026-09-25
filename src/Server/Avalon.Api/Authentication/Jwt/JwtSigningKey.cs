using System.Security.Cryptography;
using System.Text;
using Avalon.Api.Config;
using Microsoft.IdentityModel.Tokens;

namespace Avalon.Api.Authentication.Jwt;

/// <summary>
/// The one place the api turns <see cref="AuthenticationConfig.IssuerSigningKey"/> into the key that
/// signs (<see cref="JwtUtils"/>) and validates (<see cref="ServiceRegistration.AddAuth"/>) its JWTs.
/// The key is never committed (#482): it comes from user-secrets in development and from the
/// environment everywhere else, and startup refuses to go on without a usable one.
/// </summary>
public static class JwtSigningKey
{
    public const string SettingName = "Application:Authentication:IssuerSigningKey";
    public const string EnvironmentVariableName = "Application__Authentication__IssuerSigningKey";

    /// <summary>HMAC-SHA256 needs a key at least as long as its 256-bit output.</summary>
    public const int MinimumBytes = 32;

    // SHA-256 of keys that have been public and must never sign again: the one committed to
    // appsettings.json until #482. Only the hash is kept, so the key itself stays out of the code.
    public static readonly IReadOnlyList<string> BlockedKeyHashes =
    [
        "99e2138407b7f8aa4be292593a9432ad73a5b869d19a22d34cc3b63e1552c645",
    ];

    private static readonly string HowToSet =
        $"Set {SettingName} to a random value of at least {MinimumBytes} bytes: in development run " +
        $"`dotnet user-secrets set \"{SettingName}\" \"<key>\" --project src/Server/Avalon.Api`, " +
        $"elsewhere set the environment variable {EnvironmentVariableName}. See CLAUDE.md, \"REST API signing key\".";

    /// <summary>
    /// The signing key for <paramref name="config"/>, or an <see cref="InvalidOperationException"/>
    /// naming the setting when the key is missing, has leading or trailing whitespace, is shorter
    /// than <see cref="MinimumBytes"/> bytes in UTF-8, or is one of <see cref="BlockedKeyHashes"/>.
    /// </summary>
    public static SymmetricSecurityKey Create(AuthenticationConfig? config) => Create(config, BlockedKeyHashes);

    /// <summary>
    /// <see cref="Create(AuthenticationConfig?)"/> against <paramref name="blockedHashes"/> (lower-case
    /// hex SHA-256 of the UTF-8 key) instead of <see cref="BlockedKeyHashes"/>, so tests can block a
    /// made-up key. Public rather than internal: Avalon.Api is strong-named and the test assembly is
    /// not, so InternalsVisibleTo is not available (the repository's convention for such seams), and
    /// a caller passing its own list blocks nothing it could not already choose not to configure.
    /// </summary>
    public static SymmetricSecurityKey Create(AuthenticationConfig? config, IReadOnlyCollection<string> blockedHashes)
    {
        string? key = config?.IssuerSigningKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException($"The JWT signing key is not set. {HowToSet}");
        }

        // Refused rather than trimmed: a newline from a key file would otherwise sign with bytes
        // nobody meant, and would slip a blocked key past the hash check below.
        if (!string.Equals(key, key.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The JWT signing key has leading or trailing whitespace, often a newline from the file it " +
                $"was read from. Remove it. {HowToSet}");
        }

        byte[] bytes = Encoding.UTF8.GetBytes(key);
        if (bytes.Length < MinimumBytes)
        {
            throw new InvalidOperationException(
                $"The JWT signing key is {bytes.Length} bytes; HMAC-SHA256 needs at least {MinimumBytes}. {HowToSet}");
        }

        string hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (blockedHashes.Contains(hash, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "The JWT signing key is one that was committed to the repository and is public; " +
                $"anyone could forge tokens with it. Generate a new one. {HowToSet}");
        }

        return new SymmetricSecurityKey(bytes);
    }
}
