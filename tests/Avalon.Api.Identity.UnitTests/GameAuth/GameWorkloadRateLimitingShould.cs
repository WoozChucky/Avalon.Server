using System.Security.Claims;
using Avalon.Api.Hosting.Middlewares;
using Avalon.Api.Identity.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Avalon.Api.Identity.UnitTests.GameAuth;

public sealed class GameWorkloadRateLimitingShould
{
    // The workloads the game workload scheme authenticates, as the api registers them for the rate limiter (#794).
    private static readonly IServiceProvider s_services = new ServiceCollection()
        .AddSingleton<IRateLimitWorkloads, GameServerRateLimitWorkloads>()
        .BuildServiceProvider();

    [Fact]
    public void Partition_internal_traffic_by_authenticated_workload_instead_of_shared_player_source()
    {
        var context = new DefaultHttpContext { RequestServices = s_services };
        context.User = new(new ClaimsIdentity([new Claim(GameServerAuthHandler.ServerIdClaim, "world-1")], GameServerAuthHandler.Scheme));
        ApiRateLimiting.Partition partition = ApiRateLimiting.PartitionOf(context, true);
        Assert.Equal(ApiRateLimiting.PartitionKind.Workload, partition.Kind);
        Assert.Equal("world-1", partition.Key);
    }
    [Fact]
    public void Never_treat_a_server_header_or_player_identity_as_workload_authentication()
    {
        var context = new DefaultHttpContext { RequestServices = s_services };
        context.Request.Headers["X-Server-Id"] = "world-1";
        Assert.Equal(ApiRateLimiting.PartitionKind.Anonymous, ApiRateLimiting.PartitionOf(context, true).Kind);
        context.User = new(new ClaimsIdentity([new Claim(GameServerAuthHandler.ServerIdClaim, "world-1"), new Claim(ClaimTypes.NameIdentifier, "7")], "Bearer"));
        Assert.NotEqual(ApiRateLimiting.PartitionKind.Workload, ApiRateLimiting.PartitionOf(context, true).Kind);
    }
}
