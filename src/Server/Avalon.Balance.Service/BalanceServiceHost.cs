using System.Text.Json;
using System.Text.Json.Serialization;
using Avalon.Balance.Core;
using Avalon.Balance.Data;
using Avalon.Balance.Service.Endpoints;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.ResponseCompression;

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

        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            o.SerializerOptions.DictionaryKeyPolicy = null;
            o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        });

        builder.Services.AddResponseCompression(o =>
        {
            o.EnableForHttps = true;
            o.Providers.Clear();
            o.Providers.Add<GzipCompressionProvider>();
        });

        WebApplication app = builder.Build();

        app.MapDefaultEndpoints();
        app.UseMiddleware<SharedSecretMiddleware>();
        app.Use((context, next) =>
        {
            IHttpMaxRequestBodySizeFeature? feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (feature is { IsReadOnly: false })
                feature.MaxRequestBodySize = MaxRequestBodyBytes;
            return next(context);
        });
        app.UseResponseCompression();
        app.MapCatalogEndpoints();

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
