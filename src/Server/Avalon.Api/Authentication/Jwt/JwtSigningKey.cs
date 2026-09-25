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
    private static readonly string[] PublicKeyHashes =
    [
        "99e2138407b7f8aa4be292593a9432ad73a5b869d19a22d34cc3b63e1552c645",
    ];

    private static readonly string HowToSet =
        $"Set {SettingName} to a random value of at least {MinimumBytes} bytes: in development run " +
        $"`dotnet user-secrets set \"{SettingName}\" \"<key>\" --project src/Server/Avalon.Api`, " +
        $"elsewhere set the environment variable {EnvironmentVariableName}. See CLAUDE.md, \"REST API signing key\".";

    /// <summary>
    /// The signing key for <paramref name="config"/>, or an <see cref="InvalidOperationException"/>
    /// naming the setting when the key is missing, shorter than <see cref="MinimumBytes"/> bytes in
    /// UTF-8, or one known to be public.
    /// </summary>
    public static SymmetricSecurityKey Create(AuthenticationConfig? config)
    {
        string? key = config?.IssuerSigningKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException($"The JWT signing key is not set. {HowToSet}");
        }

        byte[] bytes = Encoding.UTF8.GetBytes(key);
        if (bytes.Length < MinimumBytes)
        {
            throw new InvalidOperationException(
                $"The JWT signing key is {bytes.Length} bytes; HMAC-SHA256 needs at least {MinimumBytes}. {HowToSet}");
        }

        string hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (PublicKeyHashes.Contains(hash, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "The JWT signing key is one that was committed to the repository and is public; " +
                $"anyone could forge tokens with it. Generate a new one. {HowToSet}");
        }

        return new SymmetricSecurityKey(bytes);
    }
}
