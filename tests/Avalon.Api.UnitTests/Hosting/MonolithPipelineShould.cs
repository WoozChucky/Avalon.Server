using Avalon.Api.Testing;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Avalon.Api.UnitTests.Hosting;

/// <summary>
/// The API's services run the middleware Avalon.Api's Program.cs ran, in its order (#794): identity's Steam OpenID
/// callback before authentication and its game workload authentication after it, at the hooks of the one pipeline,
/// and the world routes the worlds service declares. Identity's hook starts with the game workload routes' port check
/// (design D7.4).
/// </summary>
public sealed class MonolithPipelineShould
{
    [Fact]
    public async Task Run_the_middleware_of_Program_in_its_order()
    {
        List<string> names = await MiddlewareRecorder.RecordAsync(Environments.Production, ApiServices.All);

        Assert.Equal(
        [
            "ExceptionHandlerMiddleware",
            "RequestLoggingMiddleware",
            "ForwardedHeadersSetup",
            "ForwardedHeadersMiddleware",
            "EndpointRoutingMiddleware",
            "CorsMiddleware",
            "GameInternalRoutes",
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
