using System.Text.Json;
using System.Text.Json.Serialization;
using Avalon.Api;
using Avalon.Api.Config;
using Avalon.Api.Converters;
using Avalon.Api.Middlewares;
using Avalon.Api.Services;
using Avalon.Api.Worlds;
using Avalon.Hosting;
using Avalon.Hosting.Extensions;
using Avalon.Infrastructure;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Host.UseDefaultServiceProvider((_, options) => AvalonServiceProvider.Configure(options));

IConfiguration configuration = ApiConfiguration.Sources(builder);

builder.AddLoggingAndServiceDefaults(configuration);

IServiceCollection services = builder.Services;

// Add services to the container.
{
    ApplicationConfig applicationConfig = ApiConfiguration.Bind(configuration);
    services.AddSingleton(applicationConfig);
    services.AddSingleton(applicationConfig.Environment!);
    services.AddSingleton(applicationConfig.Authentication!);
    services.AddSingleton(applicationConfig.Notification!);
    services.AddSingleton(applicationConfig.Cache!);

    services.Configure<CookiePolicyOptions>(options => { options.MinimumSameSitePolicy = SameSiteMode.None; });

    services.AddCors();

    services.AddHttpContextAccessor();
    services.AddControllers()
        .AddJsonOptions(options =>
        {
            // No ValueObject converter: nothing on this surface is a value object. Every DTO takes
            // the primitive and its mapper unwraps with .Value, which ApiContractShould holds them
            // to. Reinstate Avalon.Common.Converters.ValueObjectJsonConverterFactory here if that
            // ever changes -- that test failing is the signal.
            options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });

    services.AddOpenApi(options =>
    {
        options.AddDocumentTransformer((document, context, arg3) =>
        {
            document.Info = new OpenApiInfo
            {
                Title = "Avalon.Api",
                Version = "v1",
                Description = "The official API for Avalon.",
                Contact = new OpenApiContact {Name = "Avalon Project", Url = new Uri("https://avalon.monster")},
                License = new OpenApiLicense {Name = "MIT", Url = new Uri("https://opensource.org/license/mit/")},
                TermsOfService = new Uri("https://avalon.monster/terms")
            };
            return Task.CompletedTask;
        });
        options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
        options.CreateSchemaReferenceId = type => type.Type.FullName!;
    });
    services.AddAuth(applicationConfig);
    services.AddInfrastructure(applicationConfig);
    // Application:Email (#510): no sender unless configured, and then email change is off (501).
    services.AddEmail(applicationConfig.Email, builder.Environment);
}

// Microsoft.Extensions.ApiDescription.Server generates the OpenAPI document by invoking this
// Main and interrupting it once the host starts, so everything between Build() and RunAsync()
// runs for real: migrations, workers, the cache connection. The docs workflow has no Postgres
// and no Redis, and the document needs neither, so it sets this and the startup work below is
// skipped. Nothing else should ever set it - a process that serves traffic with this on would
// skip its own migrations.
bool openApiGenerationOnly = string.Equals(
    Environment.GetEnvironmentVariable("AVALON_OPENAPI_GENERATION_ONLY"), "true",
    StringComparison.OrdinalIgnoreCase);

WebApplication app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
{
    if (app.Environment.IsDevelopment())
    {
        app.UseDeveloperExceptionPage();
    }

    // app.UseHsts();
    app.UseMiddleware<ExceptionHandlerMiddleware>();
    app.UseMiddleware<RequestLoggingMiddleware>();

    // Loopback plus the proxies under Application:ForwardedHeaders (#478 review); a header from any
    // other peer is ignored and logged, rate-limited.
    app.UseAvalonForwardedHeaders();

    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.WithTitle("Avalon.Api");
        options.WithTheme(ScalarTheme.BluePlanet);
        options.HideSidebar();
    });

    app.UseRouting();

    app.UseCors(x => x
        .WithOrigins(
            "http://localhost:4200",
            "http://localhost:5210",
            "https://avalon.monster",
            "https://dashboard.avalon.monster"
        )
        .AllowAnyMethod()
        .AllowAnyHeader()
    );

    app.UseAuthentication();

    // After authentication, so a signed-in request is counted against its account and a JWT's
    // account has been revalidated first; before the world routes and authorization, so a flood
    // of refused requests is limited too (#561). /health and /alive are exempt.
    app.UseApiRateLimiting();

    // /world/{worldId}/... only (#523): 404 for a world this api does not serve or the caller may
    // not enter, 503 for one whose databases failed at startup. Before authorization, so an unknown
    // world is a 404 whatever the endpoint's role policy.
    app.UseMiddleware<WorldRouteMiddleware>();

    app.UseAuthorization();

    app.MapControllers();
}

ILogger<Program> logger = app.Services.GetRequiredService<ILogger<Program>>();
ForwardedHeadersSetup.WarnIfNoProxyTrusted(logger, app.Services.GetRequiredService<ApplicationConfig>().ForwardedHeaders,
    app.Environment);
Avalon.Infrastructure.Login.LoginLimitsValidation.LogAtStartup(logger,
    app.Services.GetRequiredService<Avalon.Infrastructure.Login.ILoginLimits>(), "Application:Authentication");
logger.LogInformation("Application:Email:Sender is {EmailSender}; email change is {EmailChange}",
    (app.Services.GetRequiredService<ApplicationConfig>().Email ?? new EmailConfig()).Sender,
    app.Services.GetService<Avalon.Api.Services.Email.IEmailSender>() is null ? "off (501)" : "on");

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

    IEnumerable<IWorkerService> workerServices = app.Services.GetServices<IWorkerService>();
    foreach (IWorkerService workerService in workerServices)
    {
        logger.LogInformation("Starting worker {Worker}", workerService.GetType().Name);
        await workerService.StartWorker(cts.Token);
    }

    IReplicatedCache cache = app.Services.GetRequiredService<IReplicatedCache>();
    await cache.ConnectAsync();
    app.Services.TraceRedis(cache.Connection);
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
