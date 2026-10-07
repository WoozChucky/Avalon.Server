using Avalon.Api.Testing;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Avalon.Api.Identity.UnitTests.GameAuth;

/// <summary>
/// The game workload routes, <c>/internal/game/*</c>, exist on the game workload listener only (#794, design D7.4),
/// judged by the port the connection was accepted on: a request that arrived on the port
/// <c>Kestrel:Endpoints:GameInternal:Url</c> names reaches them, and one that arrived on any other gets 404 whatever
/// its method, the case of its path, or the host it names.
/// </summary>
public sealed class InternalEndpointsShould
{
    private const int WorkloadPort = 9555;
    private const int PublicPort = 8080;
    private const string Route = "/internal/game/sessions/end";

    [Fact]
    public async Task Exist_on_the_game_workload_port_only()
    {
        await using ApiTestHost host = await ApiTestHost.StartAsync([IdentityApi.Service], new ApiTestHostOptions
        {
            Settings = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Kestrel:Endpoints:GameInternal:Url"] = $"https://0.0.0.0:{WorkloadPort}",
            },
        });

        // On the workload port the route answers: its workload authentication refuses a caller without a certificate.
        Assert.Equal(StatusCodes.Status401Unauthorized, await StatusAsync(host, HttpMethods.Post, Route, WorkloadPort));

        // Anywhere else it does not exist, for every method, in any case, though the request names the workload port.
        string[] methods =
        [
            HttpMethods.Get, HttpMethods.Post, HttpMethods.Put, HttpMethods.Patch, HttpMethods.Delete, HttpMethods.Head,
            HttpMethods.Options,
        ];
        foreach (string method in methods)
            Assert.Equal(StatusCodes.Status404NotFound, await StatusAsync(host, method, Route.ToUpperInvariant(), PublicPort));
    }

    /// <summary>
    /// The status <paramref name="method"/> <paramref name="path"/> gets on a connection accepted on
    /// <paramref name="localPort"/>, from a request that names the workload port in its Host header.
    /// </summary>
    private static async Task<int> StatusAsync(ApiTestHost host, string method, string path, int localPort)
    {
        HttpContext context = await ((TestServer)host.Services.GetRequiredService<IServer>()).SendAsync(http =>
        {
            http.Request.Method = method;
            http.Request.Path = path;
            http.Request.Host = new HostString("avalon-api.avalon.svc", WorkloadPort);
            http.Connection.LocalPort = localPort;
        });
        return context.Response.StatusCode;
    }
}
