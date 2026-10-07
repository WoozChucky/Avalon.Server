using Avalon.Api.Hosting.Middlewares;

namespace Avalon.Api.Identity.Authentication;

/// <summary>
/// The game servers the workload scheme authenticated (<see cref="GameServerAuthHandler"/>), each counted in the rate
/// limiter's workload partition under its own server id. Only the TLS peer certificate makes a request a workload's:
/// a player token that carries the claim, or a server-id header, does not.
/// </summary>
public sealed class GameServerRateLimitWorkloads : IRateLimitWorkloads
{
    public string? WorkloadOf(HttpContext context) =>
        context.User.Identity?.AuthenticationType == GameServerAuthHandler.Scheme
            ? context.User.FindFirst(GameServerAuthHandler.ServerIdClaim)?.Value
            : null;
}
