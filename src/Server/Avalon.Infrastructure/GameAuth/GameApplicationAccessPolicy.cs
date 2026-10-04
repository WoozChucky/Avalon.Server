using Avalon.Configuration;
using Microsoft.Extensions.Options;

namespace Avalon.Infrastructure.GameAuth;

public sealed class GameApplicationAccessPolicy(IOptions<StoreAuthenticationConfiguration> options)
{
    public bool AllowsApplication(uint appId) => options.Value.ResolveSteamApplication(appId) is not null;
    public bool AllowsWorld(uint appId, ushort worldId) => options.Value.ResolveSteamApplication(appId)?.AllowsWorld(worldId) == true;
}
