using Avalon.Api.Hosting;
using Avalon.Api.Identity.Authentication;
using Avalon.Configuration;
using Microsoft.Extensions.Options;

namespace Avalon.Api.Identity.Config;

/// <summary>
/// Store authentication's options (the publisher key, the app id) and the Steam web link's URLs, checked before the
/// api serves rather than as options validations: OpenAPI generation needs neither (#794 keeps that order: after the
/// options and Database:Worlds, before any database call).
/// </summary>
public sealed class IdentityStartupCheck : IApiStartupCheck
{
    public void Check(IServiceProvider services)
    {
        _ = services.GetRequiredService<IOptions<StoreAuthenticationConfiguration>>().Value;

        services.GetRequiredService<IOptions<SteamWebLinkOptions>>().Value.Validate();
    }
}
