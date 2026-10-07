using Avalon.Api.Testing;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Avalon.Api.UnitTests.Hosting;

/// <summary>
/// The monolith runs the middleware Avalon.Api's Program.cs ran, in its order (#794): the Steam OpenID callback
/// before authentication and the game workload authentication after it, at the hooks of the one pipeline.
/// </summary>
public sealed class MonolithPipelineShould
{
    [Fact]
    public async Task Run_the_middleware_of_Program_in_its_order()
    {
        List<string> names = await MiddlewareRecorder.RecordAsync(Environments.Production, [MonolithApi.Service]);

        Assert.Equal(
        [
            "ExceptionHandlerMiddleware",
            "RequestLoggingMiddleware",
            "ForwardedHeadersSetup",
            "ForwardedHeadersMiddleware",
            "EndpointRoutingMiddleware",
            "CorsMiddleware",
            "SteamOpenIdCallbackMiddleware",
            "AuthenticationMiddleware",
            "GameWorkloadAuthentication",
            "ApiRateLimiting",
            "RateLimitingMiddleware",
            "WorldRouteMiddleware",
            "AuthorizationMiddlewareInternal",
        ], names);
    }
}
