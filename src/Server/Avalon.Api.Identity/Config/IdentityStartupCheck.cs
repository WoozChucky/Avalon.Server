using Avalon.Api.Hosting;
using Avalon.Api.Hosting.Authentication.Jwt;
using Avalon.Api.Identity.Authentication;
using Avalon.Configuration;
using Microsoft.Extensions.Options;

namespace Avalon.Api.Identity.Config;

/// <summary>
/// Store authentication's options (the publisher key, the app id), the Steam web link's URLs and the game-auth host key,
/// checked before the api serves rather than as options validations: OpenAPI generation needs none of them (#794 keeps
/// that order: after the options and Database:Worlds, before any database call).
/// </summary>
public sealed class IdentityStartupCheck(ILogger<IdentityStartupCheck> logger) : IApiStartupCheck
{
    public void Check(IServiceProvider services)
    {
        _ = services.GetRequiredService<IOptions<StoreAuthenticationConfiguration>>().Value;

        services.GetRequiredService<IOptions<SteamWebLinkOptions>>().Value.Validate();

        if (services.GetRequiredService<GameAuthHostKey>().FromIssuerSigningKey)
        {
            logger.LogWarning(
                "{HostKeySetting} is not set, so the game-auth cryptography derives its keys from {IssuerSigningKeySetting}, " +
                "as it did before #801. Set the host key to that same value: a later release stops falling back.",
                GameAuthConfig.HostKeySetting, JwtSigningKey.SettingName);
        }
    }
}
