using System.Text.Json.Nodes;
using Avalon.Api.Config;
using Avalon.Api.Controllers;
using Avalon.Api.UnitTests.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Scalar.AspNetCore;

namespace Avalon.Api.UnitTests.Contracts;

/// <summary>
/// An in-memory api that maps what Program.cs maps (<c>/health</c>, <c>/alive</c>, the OpenAPI document, Scalar and
/// every controller), with Program's OpenAPI setup and authentication schemes and nothing below the controllers:
/// enough to read its endpoints, the document it serves and which endpoint a request reaches, not to answer API
/// requests (#794).
/// </summary>
public sealed class ContractHost : IAsyncDisposable
{
    /// <summary>A request carrying this header stops once routing has chosen its endpoint, which does not run.</summary>
    private const string ProbeHeader = "X-Route-Probe";

    private readonly WebApplication _app;

    private ContractHost(WebApplication app) => _app = app;

    /// <summary>Every endpoint the host maps, as routing sees them.</summary>
    public IReadOnlyList<Endpoint> Endpoints => _app.Services.GetRequiredService<EndpointDataSource>().Endpoints;

    public static async Task<ContractHost> StartAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.AddDefaultHealthChecks();
        builder.Services.AddControllers().AddApplicationPart(typeof(AccountController).Assembly);
        builder.Services.AddAvalonOpenApi();
        // Program's authentication schemes: the document carries the bearer scheme only when one is registered.
        builder.Services.AddAuth(new ApplicationConfig { Authentication = ApiAuthHost.AuthConfig });

        WebApplication app = builder.Build();
        app.UseRouting();
        app.Use((context, next) => context.Request.Headers.ContainsKey(ProbeHeader) ? Task.CompletedTask : next(context));
        app.MapDefaultEndpoints();
        app.MapOpenApi();
        app.MapScalarApiReference();
        app.MapControllers();
        await app.StartAsync();
        return new ContractHost(app);
    }

    /// <summary>The endpoint routing chooses for <paramref name="method"/> <paramref name="path"/>, or null when none matches.</summary>
    public async Task<Endpoint?> EndpointReachedAsync(string method, string path)
    {
        HttpContext probed = await _app.GetTestServer().SendAsync(http =>
        {
            http.Request.Method = method;
            http.Request.Path = path;
            http.Request.Headers[ProbeHeader] = "1";
        });
        return probed.GetEndpoint();
    }

    /// <summary>The document the host serves at <c>/openapi/v1.json</c>.</summary>
    public async Task<JsonNode> DocumentAsync()
    {
        using HttpClient client = _app.GetTestClient();
        return JsonNode.Parse(await client.GetStringAsync("/openapi/v1.json"))!;
    }

    public ValueTask DisposeAsync() => _app.DisposeAsync();
}
