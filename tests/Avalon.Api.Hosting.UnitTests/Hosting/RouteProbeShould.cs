using System.Net;
using Avalon.Api.Testing;
using Xunit;

namespace Avalon.Api.Hosting.UnitTests.Hosting;

/// <summary>
/// The test host's route probe (#794, design section 11.2): once routing has chosen an endpoint the request is
/// answered 204, naming the endpoint's route, and the endpoint does not run; a request no endpoint matches is a 404.
/// </summary>
public sealed class RouteProbeShould : IAsyncLifetime
{
    private ApiTestHost _host = null!;

    public async Task InitializeAsync() => _host = await ApiTestHost.StartAsync([], new ApiTestHostOptions { ProbeRoutes = true });

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Name_the_route_a_request_matches_without_running_its_endpoint()
    {
        using HttpResponseMessage response = await _host.Client.GetAsync("/anonymous");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("/anonymous", Assert.Single(response.Headers.GetValues(ApiTestHost.MatchedRouteHeader)));
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Probe_ahead_of_authentication()
    {
        using HttpResponseMessage response = await _host.Client.GetAsync("/admin");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("/admin", Assert.Single(response.Headers.GetValues(ApiTestHost.MatchedRouteHeader)));
    }

    [Fact]
    public async Task Answer_404_when_no_endpoint_matches()
    {
        using HttpResponseMessage response = await _host.Client.GetAsync("/no/such/route");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(response.Headers.Contains(ApiTestHost.MatchedRouteHeader));
    }
}
