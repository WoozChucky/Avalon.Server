using System.Net;
using Avalon.Api.Identity.Authentication;
using Avalon.Api.Testing;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace Avalon.Api.Identity.UnitTests.GameAuth;

/// <summary>
/// The game workload routes, <c>/internal/game/*</c>, are mapped on the game workload listener only (#794, design
/// D7.4): a request reaches them on that listener's port, the port <c>Kestrel:Endpoints:GameInternal:Url</c> names,
/// and reaches no endpoint on any other, the public listener the ingress forwards to included.
/// </summary>
public sealed class InternalEndpointsShould
{
    [Theory]
    [InlineData(null, GameWorkloadHosting.DefaultPort)]
    [InlineData("https://0.0.0.0:9555", 9555)]
    public async Task Match_the_internal_routes_on_the_game_workload_port_only(string? workloadUrl, int workloadPort)
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync([IdentityApi.Service], new ApiTestHostOptions
        {
            ProbeRoutes = true,
            Settings = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Kestrel:Endpoints:GameInternal:Url"] = workloadUrl,
            },
        });
        RouteEndpoint[] routes = host.Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("internal/", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.NotEmpty(routes);
        foreach (RouteEndpoint route in routes)
        {
            string method = route.Metadata.GetRequiredMetadata<HttpMethodMetadata>().HttpMethods[0];
            string path = "/" + route.RoutePattern.RawText;

            Assert.Equal(HttpStatusCode.NoContent, await ProbeAsync(host, method, $"https://avalon-api.avalon.svc:{workloadPort}{path}"));
            Assert.Equal(HttpStatusCode.NotFound, await ProbeAsync(host, method, $"http://avalon-api.avalon.svc:8080{path}"));
            Assert.Equal(HttpStatusCode.NotFound, await ProbeAsync(host, method, $"https://avalon.example.test{path}"));
        }
    }

    /// <summary>The status the route probe answers: 204 when the request reached an endpoint, 404 when it reached none.</summary>
    private static async Task<HttpStatusCode> ProbeAsync(ApiTestHost host, string method, string url)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        using HttpResponseMessage response = await host.Client.SendAsync(request);
        return response.StatusCode;
    }
}
