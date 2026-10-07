using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalon.Api.Hosting.Authentication;
using Avalon.Api.Hosting.Config;
using Avalon.Api.Hosting.Middlewares;
using Avalon.Hosting;
using Avalon.Hosting.Extensions;
using Avalon.Infrastructure;
using Microsoft.AspNetCore.Mvc.ApplicationParts;

namespace Avalon.Api.Hosting;

/// <summary>
/// The host every API process runs (#794, design section 3.1): one builder and one pipeline for the services it is
/// given (<see cref="IApiService"/>), so each runs as it did when Avalon.Api was one service.
/// </summary>
public static class AvalonApiHost
{
    /// <summary>
    /// Set to <c>true</c> by the docs build only: the host describes the API and serves nothing. Everything between
    /// <c>Build()</c> and the run would otherwise run for real (migrations, workers, the cache connection), because
    /// Microsoft.Extensions.ApiDescription.Server generates the OpenAPI document by running the entry point and
    /// interrupting it once the host starts; the docs workflow has no Postgres and no Redis, and the document needs
    /// neither. Nothing else should ever set it: a process that serves traffic with it on would skip its own
    /// migrations.
    /// </summary>
    public const string OpenApiGenerationOnlyVariable = "AVALON_OPENAPI_GENERATION_ONLY";

    /// <summary>
    /// Builds the host for <paramref name="services"/>, runs its startup work and serves until the process is told to
    /// stop.
    /// </summary>
    public static async Task RunAsync(string[] args, params IApiService[] services)
    {
        WebApplicationBuilder builder = CreateBuilder(args, services);

        bool openApiGenerationOnly = string.Equals(
            Environment.GetEnvironmentVariable(OpenApiGenerationOnlyVariable), "true",
            StringComparison.OrdinalIgnoreCase);

        WebApplication app = builder.Build();

        app.UseAvalonApi(services);

        // The category Program.cs logged its startup lines under before the host moved here.
        ILogger logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Program");
        ForwardedHeadersSetup.WarnIfNoProxyTrusted(logger,
            app.Configuration.GetSection(ForwardedHeadersSetup.Section).Get<ForwardedHeadersConfig>(), app.Environment);
        foreach (IApiService service in services)
            service.LogStartup(app.Services, logger);

        CancellationTokenSource cts = new();

        if (openApiGenerationOnly)
        {
            logger.LogWarning(
                "AVALON_OPENAPI_GENERATION_ONLY is set: skipping migrations, workers and the cache " +
                "connection. This process can describe the API but not serve it.");
        }
        else
        {
            await ApiStartup.ValidateAndMigrateAsync(app.Services, logger);

            foreach (IApiService service in services)
                await service.StartAsync(app.Services, cts.Token);

            if (ApiServiceNeeds.Union(services.Select(service => service.Needs)).Redis)
            {
                IReplicatedCache cache = app.Services.GetRequiredService<IReplicatedCache>();
                await cache.ConnectAsync();
                app.Services.TraceRedis(cache.Connection);
            }
        }

        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            logger.LogError(eventArgs.ExceptionObject as Exception, "Unhandled exception");
            Environment.Exit(1);
        };
        AppDomain.CurrentDomain.ProcessExit += async (_, _) =>
        {
            await cts.CancelAsync();
            logger.LogInformation("Exited successfully");
        };
        Console.CancelKeyPress += (_, _) => { logger.LogInformation("Ctrl+C was pressed, stopping application..."); };

        await app.RunAsync();
    }

    /// <summary>The builder for <paramref name="services"/>, from the command line's arguments.</summary>
    public static WebApplicationBuilder CreateBuilder(string[] args, IReadOnlyList<IApiService> services) =>
        CreateBuilder(new WebApplicationOptions { Args = args }, services, configure: null);

    /// <summary>
    /// The builder for <paramref name="services"/>: the container settings every Avalon host builds with, the
    /// configuration (<see cref="ApiConfiguration.Sources"/>), the services' own builder settings, the logging and
    /// the service defaults, CORS, the controllers of the services' assemblies only, with camelCase JSON, the OpenAPI
    /// document, the token validation, the shared hosting for the services' needs, and then each service's own
    /// registrations. <paramref name="configure"/> runs first, on the bare builder: a test host's server, logging and
    /// settings.
    /// </summary>
    public static WebApplicationBuilder CreateBuilder(WebApplicationOptions options, IReadOnlyList<IApiService> services,
        Action<WebApplicationBuilder>? configure)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(options);
        configure?.Invoke(builder);
        builder.Host.UseDefaultServiceProvider((_, provider) => AvalonServiceProvider.Configure(provider));

        IConfiguration configuration = ApiConfiguration.Sources(builder);

        foreach (IApiService service in services)
            service.ConfigureBuilder(builder);

        builder.AddApiLoggingAndServiceDefaults(configuration);

        var needs = ApiServiceNeeds.Union(services.Select(service => service.Needs));
        IServiceCollection collection = builder.Services;
        collection.Configure<CookiePolicyOptions>(cookies => { cookies.MinimumSameSitePolicy = SameSiteMode.None; });
        collection.AddCors();
        collection.AddHttpContextAccessor();
        collection.AddControllers()
            .AddJsonOptions(json =>
            {
                // No ValueObject converter: nothing on this surface is a value object. Every DTO takes
                // the primitive and its mapper unwraps with .Value, which ApiContractShould holds them
                // to. Reinstate Avalon.Common.Converters.ValueObjectJsonConverterFactory here if that
                // ever changes -- that test failing is the signal.
                json.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                json.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            })
            .ConfigureApplicationPartManager(parts =>
            {
                // The services' controllers and no others: not those of whatever assembly started the process.
                parts.ApplicationParts.Clear();
                foreach (Assembly assembly in services.Select(service => service.ControllerAssembly).Distinct())
                {
                    foreach (ApplicationPart part in ApplicationPartFactory.GetApplicationPartFactory(assembly).GetApplicationParts(assembly))
                        parts.ApplicationParts.Add(part);
                }
            });
        collection.AddAvalonOpenApi();
        collection.AddApiAuthentication(configuration.GetSection(TokenValidationConfig.Section).Get<TokenValidationConfig>());
        collection.AddApiHosting(needs, configuration.GetSection(ForwardedHeadersSetup.Section).Get<ForwardedHeadersConfig>());

        foreach (IApiService service in services)
            service.AddServices(builder);

        return builder;
    }

    /// <summary>
    /// Serilog first, then the service defaults (#562): AddCustomLogging clears every logging
    /// provider registered before it, so the OpenTelemetry one AddServiceDefaults adds must come
    /// after it, as it does on the auth and world servers. The #558 EF rules reach both.
    /// </summary>
    public static void AddApiLoggingAndServiceDefaults(this WebApplicationBuilder builder, IConfiguration configuration)
    {
        builder.Services.AddCustomLogging(configuration);
        builder.AddServiceDefaults();
    }
}
