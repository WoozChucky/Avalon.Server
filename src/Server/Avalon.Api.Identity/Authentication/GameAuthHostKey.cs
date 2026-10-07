using Avalon.Api.Hosting.Authentication.Jwt;
using Avalon.Api.Hosting.Config;
using Avalon.Api.Identity.Config;
using Avalon.Infrastructure.GameAuth;

namespace Avalon.Api.Identity.Authentication;

/// <summary>
/// The key <see cref="GameAuthCryptography"/> derives its proof, replay receipt and Steam OpenID state keys from (#801,
/// design D4.3): <see cref="GameAuthConfig.HostKey"/>, holding the bytes the HS256 signing key held, so what was
/// protected before stays readable. For this release an unset host key falls back to
/// <see cref="TokenValidationConfig.IssuerSigningKey"/>, the key they were derived from before, and identity warns at
/// startup (<see cref="IdentityStartupCheck"/>). Either follows the rules of <see cref="JwtSigningKey.SecretBytes"/>.
/// </summary>
public sealed class GameAuthHostKey
{
    private GameAuthHostKey(byte[] bytes, bool fromIssuerSigningKey)
    {
        Bytes = bytes;
        FromIssuerSigningKey = fromIssuerSigningKey;
    }

    public byte[] Bytes { get; }

    /// <summary>Whether the key came from <see cref="TokenValidationConfig.IssuerSigningKey"/>, the host key being unset.</summary>
    public bool FromIssuerSigningKey { get; }

    /// <summary>
    /// The host key, or the HS256 signing key when the host key is unset; an <see cref="InvalidOperationException"/>
    /// naming <see cref="GameAuthConfig.HostKeySetting"/> when neither is set, or naming the setting whose value breaks
    /// the rules.
    /// </summary>
    public static GameAuthHostKey From(GameAuthConfig? gameAuth, TokenValidationConfig? authentication) =>
        From(gameAuth, authentication, JwtSigningKey.BlockedKeyHashes);

    /// <summary><see cref="From(GameAuthConfig?, TokenValidationConfig?)"/> against <paramref name="blockedHashes"/>, as <see cref="JwtSigningKey.Create(TokenValidationConfig?, IReadOnlyCollection{string})"/>.</summary>
    public static GameAuthHostKey From(GameAuthConfig? gameAuth, TokenValidationConfig? authentication,
        IReadOnlyCollection<string> blockedHashes)
    {
        string? hostKey = gameAuth?.HostKey;
        string? issuerSigningKey = authentication?.IssuerSigningKey;
        if (string.IsNullOrEmpty(hostKey) && !string.IsNullOrEmpty(issuerSigningKey))
            return new GameAuthHostKey(JwtSigningKey.SecretBytes(issuerSigningKey, JwtSigningKey.SettingName, blockedHashes), true);

        return new GameAuthHostKey(JwtSigningKey.SecretBytes(hostKey, GameAuthConfig.HostKeySetting, blockedHashes), false);
    }
}
