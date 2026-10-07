using Avalon.Api.Hosting;
using Avalon.Api.Hosting.Routing;
using Avalon.Api.Testing;
using Avalon.Api.UnitTests.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace Avalon.Api.UnitTests.Routing;

/// <summary>
/// No route is lost when the API runs as one process per service (#794, design D7.2). A request for each endpoint the
/// all-in-one process maps, at the endpoint's sample path, goes where the ingress sends it: to the owner the route
/// manifest gives its path (the ingress strips <c>/api</c>, so the services see the paths they map). The process of
/// that service, running it alone, routes the request to that endpoint, and no other service's process routes it
/// anywhere; what every process maps (<c>/health</c>, <c>/alive</c>), every process routes. The processes run as in
/// production, so they map no API docs (#803, RouteOwnershipShould). How the chart renders the manifest as ingress rules
/// is the chart's own test, and which port may reach the
/// game workload routes, which identity routes too, is InternalEndpointsShould's.
/// </summary>
public sealed class EveryRouteReachableShould
{
    private const string Manifest = "src/Server/Avalon.Api/Helm/avalon-api/files/routes.json";

    [Fact]
    public async Task Route_each_request_to_its_endpoint_in_its_owners_process_and_in_no_other()
    {
        var routes = RouteTable.Load(RepositoryRoot.PathOf(Manifest));
        Request[] requests;
        await using (WebApplication api = ApiProcess.Build(service: null))
            requests = ApiProcess.Endpoints(api).Select(Request.For).ToArray();
        Assert.NotEmpty(requests);

        var processes = new Dictionary<string, ApiTestHost>(StringComparer.Ordinal);
        try
        {
            foreach (IApiService service in ApiServices.All)
                processes[service.Name] = await ApiTestHost.StartAsync([service], new ApiTestHostOptions { ProbeRoutes = true });

            List<string> failures = [];
            foreach (Request request in requests)
            {
                string owner = routes.OwnerOf(request.Path);
                foreach ((string service, ApiTestHost process) in processes)
                {
                    string? reached = await ReachedAsync(process, request);
                    string? expected = request.EveryProcess || service == owner ? request.Route : null;
                    if (reached != expected)
                    {
                        failures.Add($"{request.Method} {request.Path} reaches {reached ?? "nothing"} in the {service} process,"
                            + $" not {expected ?? "nothing"} (the manifest sends it to {owner})");
                    }
                }
            }

            Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        }
        finally
        {
            foreach (ApiTestHost process in processes.Values)
                await process.DisposeAsync();
        }
    }

    /// <summary>The route of the endpoint <paramref name="process"/> routes <paramref name="request"/> to, or null when none.</summary>
    private static async Task<string?> ReachedAsync(ApiTestHost process, Request request)
    {
        using var message = new HttpRequestMessage(new HttpMethod(request.Method), request.Path);
        using HttpResponseMessage response = await process.Client.SendAsync(message);
        return response.Headers.TryGetValues(ApiTestHost.MatchedRouteHeader, out IEnumerable<string>? route)
            ? Normalised(route.Single())
            : null;
    }

    private static string Normalised(string route) => "/" + route.TrimStart('/');

    /// <summary>
    /// A request for an endpoint: the method it takes (GET for any), its sample path, its route, and whether the host
    /// maps it in every process rather than a service from its controllers.
    /// </summary>
    private sealed record Request(string Method, string Path, string Route, bool EveryProcess)
    {
        public static Request For(RouteEndpoint endpoint) => new(
            endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.FirstOrDefault() ?? HttpMethods.Get,
            RouteSamples.PathFor(endpoint.RoutePattern),
            Normalised(endpoint.RoutePattern.RawText ?? ""),
            endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is null);
    }
}
