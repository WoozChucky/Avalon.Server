using System.Security.Cryptography;
using Avalon.Configuration;
using OtpNet;

namespace Avalon.Infrastructure.StoreAuth;

internal static class SteamTicketIdentity
{
    internal const int NonceCharacters = 26;

    // Stay below the native generic-string limit and omit colons to avoid typed-identity parsing.
    // A lowercase base32 nonce keeps all 128 bits and works with the shipped client's character filter.
    // The trusted app slot keeps the prefix short;
    // the exact App ID stays bound in the attempt record and Steam verification request.
    internal static string? Prefix(StoreAuthenticationConfiguration config, uint appId) =>
        config.ResolveSteamApplication(appId) is { } app
            ? (config.Environment == "production" ? "p" : "d") + (app.Restricted ? "t" : "m")
            : null;

    internal static string Create(StoreAuthenticationConfiguration config, uint appId) =>
        (Prefix(config, appId) ?? throw new ArgumentException("Unknown Steam application.", nameof(appId))) +
        Base32Encoding.ToString(RandomNumberGenerator.GetBytes(16)).TrimEnd('=').ToLowerInvariant();
}
