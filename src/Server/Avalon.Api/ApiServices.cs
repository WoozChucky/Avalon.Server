using Avalon.Api.Distribution;
using Avalon.Api.Hosting;

namespace Avalon.Api;

/// <summary>
/// The API services this host runs (#794, design section 2.2), in the order the host adds them: identity, worlds,
/// commerce, distribution. Identity, worlds and commerce are still the monolith's (<see cref="MonolithApi"/>) until
/// each moves into its own library.
/// </summary>
public static class ApiServices
{
    public static IReadOnlyList<IApiService> All { get; } = [MonolithApi.Service, DistributionApi.Service];
}
