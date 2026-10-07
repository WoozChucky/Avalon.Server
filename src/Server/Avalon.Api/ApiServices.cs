using Avalon.Api.Commerce;
using Avalon.Api.Distribution;
using Avalon.Api.Hosting;
using Avalon.Api.Worlds;

namespace Avalon.Api;

/// <summary>
/// The API services this host runs (#794, design section 2.2), in the order the host adds them: identity, worlds,
/// commerce, distribution. Identity is still the monolith's (<see cref="MonolithApi"/>) until it moves into its own
/// library.
/// </summary>
public static class ApiServices
{
    public static IReadOnlyList<IApiService> All { get; } = [MonolithApi.Service, WorldsApi.Service, CommerceApi.Service, DistributionApi.Service];
}
