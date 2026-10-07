using System.Security.Claims;
using Avalon.Api.Authentication;
using Avalon.Api.Middlewares;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Avalon.Api.UnitTests.GameAuth;

public sealed class GameWorkloadRateLimitingShould
{
    [Fact]
    public void Partition_internal_traffic_by_authenticated_workload_instead_of_shared_player_source()
    {
        var context = new DefaultHttpContext();
        context.User = new(new ClaimsIdentity([new Claim(GameServerAuthHandler.ServerIdClaim, "world-1")], GameServerAuthHandler.Scheme));
        ApiRateLimiting.Partition partition = ApiRateLimiting.PartitionOf(context, true);
        Assert.Equal(ApiRateLimiting.PartitionKind.Workload, partition.Kind);
        Assert.Equal("world-1", partition.Key);
    }
    [Fact]
    public void Never_treat_a_server_header_or_player_identity_as_workload_authentication()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Server-Id"] = "world-1";
        Assert.Equal(ApiRateLimiting.PartitionKind.Anonymous, ApiRateLimiting.PartitionOf(context, true).Kind);
        context.User = new(new ClaimsIdentity([new Claim(GameServerAuthHandler.ServerIdClaim, "world-1"), new Claim(ClaimTypes.NameIdentifier, "7")], "Bearer"));
        Assert.NotEqual(ApiRateLimiting.PartitionKind.Workload, ApiRateLimiting.PartitionOf(context, true).Kind);
    }
}
