using Avalon.Api.Hosting;
using Avalon.Api.Hosting.Worlds;
using Avalon.Api.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Avalon.Api.UnitTests.Hosting;

/// <summary>
/// An API process as a deployment configures it (#794): the host's builder for every service the API has
/// (<see cref="ApiServices.All"/>) with <c>Application:Services</c> naming the one it runs (all four when none is named),
/// the settings every process shares (<see cref="ApiTestHost.Settings"/>), a world whose databases are the ones its
/// services read, and the one pipeline, on a test server. Unlike <see cref="ApiTestHost"/> it substitutes nothing the
/// caller does not, so what a process lacks stays missing. Built, not started.
/// </summary>
internal static class ApiProcess
{
    public static WebApplication Build(IApiService? service, Action<IServiceCollection>? configure = null,
        IReadOnlyDictionary<string, string?>? settings = null)
    {
        WebApplicationBuilder builder = AvalonApiHost.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = Environments.Production }, ApiServices.All, b =>
            {
                b.WebHost.UseTestServer();
                b.Configuration.AddInMemoryCollection(SettingsFor(service));
                if (settings is not null)
                    b.Configuration.AddInMemoryCollection(settings);
            });
        builder.Logging.ClearProviders();
        configure?.Invoke(builder.Services);

        WebApplication app = builder.Build();
        app.UseAvalonApi(app.Services.GetRequiredService<ApiServiceSelection>().Services);
        return app;
    }

    /// <summary>The endpoints <paramref name="process"/> maps, which its routing chooses from once it starts.</summary>
    public static IEnumerable<RouteEndpoint> Endpoints(WebApplication process) =>
        ((IEndpointRouteBuilder)process).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>();

    private static Dictionary<string, string?> SettingsFor(IApiService? service)
    {
        var settings = new Dictionary<string, string?>(ApiTestHost.Settings, StringComparer.Ordinal);
        if (service is not null)
            settings[ApiServiceSelection.Setting + ":0"] = service.Name;

        WorldDatabaseParts parts = service?.Needs.WorldDatabases ?? WorldDatabaseParts.Both;
        if (parts.HasFlag(WorldDatabaseParts.World))
            settings["Database:Worlds:1:World:ConnectionString"] = "Host=w1";
        if (parts.HasFlag(WorldDatabaseParts.Characters))
            settings["Database:Worlds:1:Characters:ConnectionString"] = "Host=c1";
        return settings;
    }
}
