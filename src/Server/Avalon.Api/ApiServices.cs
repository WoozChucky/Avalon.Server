using Avalon.Api.Commerce;
using Avalon.Api.Distribution;
using Avalon.Api.Hosting;
using Avalon.Api.Identity;
using Avalon.Api.Worlds;

namespace Avalon.Api;

/// <summary>
/// The API services this host runs (#794, design section 2.2), in the order the host adds them: identity, worlds,
/// commerce, distribution.
/// </summary>
public static class ApiServices
{
    public static IReadOnlyList<IApiService> All { get; } = [IdentityApi.Service, WorldsApi.Service, CommerceApi.Service, DistributionApi.Service];
}
