using Avalon.Common.Accounts;
using Avalon.Configuration;
using Microsoft.Extensions.Options;

namespace Avalon.Infrastructure.GameAuth;

public sealed class GameApplicationAccessPolicy(IOptions<StoreAuthenticationConfiguration> options)
{
    public bool AllowsApplication(uint appId) => options.Value.ResolveSteamApplication(appId) is not null;
    public bool AllowsWorld(uint appId, ushort worldId) => options.Value.ResolveSteamApplication(appId)?.AllowsWorld(worldId) == true;
    public bool RequiresLicenseForWorldListing(uint appId) => options.Value.ResolveSteamApplication(appId)?.Restricted == true;

    /// <summary>World eligibility for an already verified, licensed context; never changes account or session roles.</summary>
    public bool AllowsWorldAccess(uint appId, ushort worldId, AccountAccessLevel required, AccountAccessLevel accountAccess)
    {
        var application = options.Value.ResolveSteamApplication(appId);
        if (application?.AllowsWorld(worldId) != true) return false;
        var effectiveAccess = application.Restricted ? accountAccess | AccountAccessLevel.PTR : accountAccess;
        return AccessLevels.ForWorld(required).Allows(effectiveAccess);
    }
}
