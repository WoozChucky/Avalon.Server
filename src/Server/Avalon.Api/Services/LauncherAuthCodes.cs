using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Avalon.Common.ValueObjects;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Services;

namespace Avalon.Api.Services;

/// <summary>
/// One-time authorization codes for launcher sign-in (#591, RFC 8252 with PKCE). The signed-in website
/// issues one for the launcher's PKCE challenge and loopback port; the launcher redeems it with the
/// verifier only it holds. A code lives 60 seconds and is spent by its first redemption, right or wrong.
/// </summary>
public interface ILauncherAuthCodes
{
    /// <exception cref="ArgumentException">A challenge that is not 43 base64url characters, or a port outside 1024–65535.</exception>
    Task<string> IssueAsync(AccountId accountId, int credentialsVersion, string challenge, int redirectPort);

    /// <summary>The grant, once: null for an unknown, expired, already spent or wrong-verifier code.</summary>
    Task<LauncherGrant?> RedeemAsync(string code, string verifier);
}

/// <param name="CredentialsVersion">The account's credentials version when the code was issued: a change since refuses the exchange.</param>
public sealed record LauncherGrant(AccountId AccountId, int CredentialsVersion, int RedirectPort);

public sealed class LauncherAuthCodes : ILauncherAuthCodes
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);
    public const int MinPort = 1024;
    public const int MaxPort = 65535;

    private readonly IReplicatedCache _cache;
    private readonly ISecureRandom _random;

    public LauncherAuthCodes(IReplicatedCache cache, ISecureRandom random)
    {
        _cache = cache;
        _random = random;
    }

    public async Task<string> IssueAsync(AccountId accountId, int credentialsVersion, string challenge, int redirectPort)
    {
        if (!Pkce.IsChallenge(challenge))
            throw new ArgumentException("The challenge must be 43 base64url characters (PKCE S256).", nameof(challenge));
        if (redirectPort is < MinPort or > MaxPort)
            throw new ArgumentException($"The redirect port must be between {MinPort} and {MaxPort}.", nameof(redirectPort));

        string code = Base64Url(_random.GetBytes(32));
        string value = string.Join('|', accountId.Value.ToString(CultureInfo.InvariantCulture),
            credentialsVersion.ToString(CultureInfo.InvariantCulture), challenge,
            redirectPort.ToString(CultureInfo.InvariantCulture));
        // Stored by the code's hash: a read of the cache does not yield a usable code.
        await _cache.SetAsync(CacheKeys.LauncherAuthCode(HashOf(code)), value, Lifetime);
        return code;
    }

    public async Task<LauncherGrant?> RedeemAsync(string code, string verifier)
    {
        if (string.IsNullOrEmpty(code)) return null;

        string key = CacheKeys.LauncherAuthCode(HashOf(code));
        string? value = await _cache.GetAsync(key);
        // Only the redemption whose delete wins goes on, as the email-change token does: two racing
        // exchanges of one code get one grant between them. The code is spent before the verifier is
        // looked at, so a wrong or malformed one spends it too (#591 review).
        if (value is null || !await _cache.RemoveAsync(key)) return null;
        if (!Pkce.IsVerifier(verifier)) return null;

        string[] parts = value.Split('|');
        if (parts.Length != 4) return null;
        byte[] expected = Encoding.ASCII.GetBytes(parts[2]);
        byte[] actual = Encoding.ASCII.GetBytes(Pkce.ChallengeOf(verifier));
        if (!CryptographicOperations.FixedTimeEquals(expected, actual)) return null;

        return new LauncherGrant(new AccountId(long.Parse(parts[0], CultureInfo.InvariantCulture)),
            int.Parse(parts[1], CultureInfo.InvariantCulture), int.Parse(parts[3], CultureInfo.InvariantCulture));
    }

    private static string HashOf(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));

    internal static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}

/// <summary>PKCE S256 (RFC 7636 §4.1–4.2): the only method a launcher may use.</summary>
public static class Pkce
{
    /// <summary>base64url, no padding, of a SHA-256: exactly 43 characters of [A-Za-z0-9_-].</summary>
    public static bool IsChallenge(string? s) => s is { Length: 43 } && s.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>43 to 128 characters of [A-Za-z0-9._~-].</summary>
    public static bool IsVerifier(string? s) =>
        s is { Length: >= 43 and <= 128 } && s.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~');

    public static string ChallengeOf(string verifier) =>
        LauncherAuthCodes.Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
}
