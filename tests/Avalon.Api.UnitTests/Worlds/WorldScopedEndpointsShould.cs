using System.Net;
using Avalon.Api.UnitTests.Authentication;
using Avalon.Api.Worlds;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Avalon.Api.UnitTests.Worlds;

/// <summary>
/// The world check runs only where <see cref="WorldScopedAttribute"/> is (#523), so the marker and the
/// route must agree: a route naming a world without the marker would read no world's databases
/// through no check, and a marked endpoint open to anonymous callers would reach its action with no
/// world selected.
/// </summary>
public sealed class WorldScopedEndpointsShould
{
    private static async Task<List<RouteEndpoint>> ApiEndpoints()
    {
        await using ApiAuthHost host = await ApiAuthHost.StartAsync();
        return host.Endpoints.OfType<RouteEndpoint>().ToList();
    }

    private static bool NamesAWorld(RouteEndpoint endpoint) =>
        endpoint.RoutePattern.RawText?.Contains("{" + WorldScopedAttribute.RouteValue, StringComparison.Ordinal) == true;

    private static bool IsWorldScoped(Endpoint endpoint) =>
        endpoint.Metadata.GetMetadata<WorldScopedAttribute>() is not null;

    [Fact]
    public async Task Mark_every_endpoint_whose_route_names_a_world_and_no_other()
    {
        List<RouteEndpoint> endpoints = await ApiEndpoints();

        Assert.Contains(endpoints, IsWorldScoped);
        Assert.All(endpoints, e => Assert.True(NamesAWorld(e) == IsWorldScoped(e),
            $"{e.RoutePattern.RawText} ({e.DisplayName}): route names a world = {NamesAWorld(e)}, [WorldScoped] = {IsWorldScoped(e)}"));
    }

    [Fact]
    public async Task Let_no_anonymous_caller_onto_a_world_endpoint()
    {
        List<RouteEndpoint> open = (await ApiEndpoints())
            .Where(e => IsWorldScoped(e) && e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .ToList();

        Assert.True(open.Count == 0,
            $"[WorldScoped] endpoints allowing anonymous callers: {string.Join(", ", open.Select(e => e.RoutePattern.RawText))}");
    }

    [Fact]
    public async Task Answer_404_on_a_world_endpoint_that_allows_anonymous_callers()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddAuthentication();
        builder.Services.AddAuthorization();
        await using WebApplication app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseMiddleware<WorldRouteMiddleware>();
        app.UseAuthorization();
        app.MapGet("/world/{worldId:int}/open", () => "reached")
            .WithMetadata(new WorldScopedAttribute())
            .AllowAnonymous();
        await app.StartAsync();
        using HttpClient client = app.GetTestClient();

        HttpResponseMessage response = await client.GetAsync("/world/1/open");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync());
    }
}
