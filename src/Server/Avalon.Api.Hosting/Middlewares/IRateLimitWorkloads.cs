namespace Avalon.Api.Hosting.Middlewares;

/// <summary>
/// Names the workload, a game server calling the API, that a request comes from, so the request rate limiter counts it
/// in the <see cref="ApiRateLimiting.PartitionKind.Workload"/> partition, per server, rather than as a player or as
/// its source (#794, design section 3.1). A service that authenticates workloads registers one; with none registered,
/// no request is a workload's.
/// </summary>
public interface IRateLimitWorkloads
{
    /// <summary>The workload's id, or null when the request does not come from a workload this service authenticated.</summary>
    string? WorkloadOf(HttpContext context);
}
