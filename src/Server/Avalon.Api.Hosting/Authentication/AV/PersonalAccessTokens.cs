using System.Security.Cryptography;
using System.Text;

namespace Avalon.Api.Hosting.Authentication.AV;

/// <summary>
/// How a personal access token is stored and touched, shared by the scheme that reads tokens on every API service
/// (<see cref="AvalonAuthenticationHandler"/>) and the service that mints them.
/// </summary>
public static class PersonalAccessTokens
{
    /// <summary>
    /// How stale a token's last-used time may be before a request writes it again, so a busy token costs one write a
    /// minute rather than one a request.
    /// </summary>
    public static readonly TimeSpan LastUsedBucket = TimeSpan.FromSeconds(60);

    /// <summary>The SHA-256 of the raw token's UTF-8 bytes, the only form of it the database holds.</summary>
    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
}
