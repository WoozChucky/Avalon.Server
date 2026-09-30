using Avalon.Balance.Core;
using Avalon.Balance.Data;
using Avalon.Balance.Service.Endpoints;
using Avalon.Balance.Service.Runs;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Avalon.Balance.Service;

/// <summary>The service wiring, shared by Program and the tests so they exercise the real thing.</summary>
public static class BalanceServiceHost
{
    public const long MaxRequestBodyBytes = 1024 * 1024;

    public static WebApplication Build(WebApplicationBuilder builder)
    {
        // The default console logger plus the OpenTelemetry provider AddServiceDefaults adds.
        builder.AddServiceDefaults();

        builder.Services.AddOptions<BalanceServiceOptions>()
            .BindConfiguration(BalanceServiceOptions.Section)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddSingleton(_ => LoadHost());
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<RunQueue>();
        builder.Services.AddHostedService<RunWorker>();

        // The server-wide limit: Kestrel refuses a bigger body with 413 before any handler reads it.
        // The endpoints read and write JSON with BalanceJson.Options, the one set of options, not the host's.
        builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = MaxRequestBodyBytes);

        builder.Services.AddResponseCompression(o =>
        {
            o.EnableForHttps = true;
            o.Providers.Clear();
            o.Providers.Add<GzipCompressionProvider>();
        });

        WebApplication app = builder.Build();

        app.MapDefaultEndpoints();
        app.UseMiddleware<SharedSecretMiddleware>();
        app.UseResponseCompression();
        app.MapCatalogEndpoints();
        app.MapRunEndpoints();

        return app;
    }

    private static BalanceHost LoadHost()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "balance");
        var defaults = new BalanceConfig(
            ConfigFileStore.Load(Path.Combine(dir, "scenarios.json"), ConfigFiles.ParseScenarios),
            ConfigFileStore.Load(Path.Combine(dir, "targets.json"), ConfigFiles.ParseTargets),
            ConfigFileStore.Load(Path.Combine(dir, "rotations.json"), ConfigFiles.ParseRotations));
        return new BalanceHost(SeedSource.Load(), defaults);
    }
}
