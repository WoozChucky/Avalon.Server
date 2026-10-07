using System.Text.Json.Nodes;
using Avalon.Api.Hosting;
using Avalon.Api.Identity;
using Avalon.Api.Identity.Config;
using Avalon.Api.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Avalon.Api.UnitTests.Contracts;

/// <summary>
/// An in-memory api that maps what Program.cs maps (<c>/health</c>, <c>/alive</c>, the OpenAPI document and Scalar,
/// which it serves where <see cref="ApiDocs.EnabledSetting"/> turns them on as this host does, and every controller of
/// every service, <see cref="ApiServices.All"/>), with Program's OpenAPI setup and authentication schemes and nothing
/// below the controllers: enough to read the document it serves, not to answer API requests (#794, #803).
/// </summary>
public sealed class ContractHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private ContractHost(WebApplication app) => _app = app;

    public static async Task<ContractHost> StartAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        // Production, with the docs turned on by their setting: the docs are off by default there (#803).
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [ApiDocs.EnabledSetting] = "true",
        });
        builder.AddDefaultHealthChecks();
        IMvcBuilder mvc = builder.Services.AddControllers();
        foreach (IApiService service in ApiServices.All)
            mvc.AddApplicationPart(service.ControllerAssembly);
        builder.Services.AddAvalonOpenApi();
        // Program's authentication schemes: the document carries the bearer scheme only when one is registered.
        builder.Services.AddAuth(new ApplicationConfig { Authentication = ApiTestHost.AuthConfig });

        WebApplication app = builder.Build();
        app.UseRouting();
        app.MapDefaultEndpoints();
        if (ApiDocs.AreServed(app.Environment, app.Configuration))
            ApiDocs.Map(app);
        app.MapControllers();
        await app.StartAsync();
        return new ContractHost(app);
    }

    /// <summary>The document the host serves at <c>/openapi/v1.json</c>.</summary>
    public async Task<JsonNode> DocumentAsync()
    {
        using HttpClient client = _app.GetTestClient();
        return JsonNode.Parse(await client.GetStringAsync("/openapi/v1.json"))!;
    }

    public ValueTask DisposeAsync() => _app.DisposeAsync();
}
