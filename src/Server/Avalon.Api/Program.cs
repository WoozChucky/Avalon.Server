using System.Text.Json;
using System.Text.Json.Serialization;
using Avalon.Api;
using Avalon.Api.Config;
using Avalon.Api.Converters;
using Avalon.Api.Middlewares;
using Avalon.Api.Services;
using Avalon.Database.Auth;
using Avalon.Database.Character;
using Avalon.Database.World;
using Avalon.Hosting;
using Avalon.Hosting.Extensions;
using Avalon.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Host.UseDefaultServiceProvider((_, options) => AvalonServiceProvider.Configure(options));

IConfiguration configuration = ApiConfiguration.Sources(builder);

builder.AddServiceDefaults();

IServiceCollection services = builder.Services;

services.AddCustomLogging(configuration);

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
}

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

    app.UseAuthorization();

    app.MapControllers();
}

ILogger<Program> logger = app.Services.GetRequiredService<ILogger<Program>>();
ForwardedHeadersSetup.WarnIfNoProxyTrusted(logger, app.Services.GetRequiredService<ApplicationConfig>().ForwardedHeaders,
    app.Environment);
Avalon.Infrastructure.Login.LoginLimitsValidation.LogAtStartup(logger,
    app.Services.GetRequiredService<Avalon.Infrastructure.Login.ILoginLimits>(), "Application:Authentication");

await using (AsyncServiceScope scope = app.Services.CreateAsyncScope())
{
    await using AuthDbContext authDb = await scope.ServiceProvider
        .GetRequiredService<IDbContextFactory<AuthDbContext>>().CreateDbContextAsync(CancellationToken.None);
    await using CharacterDbContext characterDb = await scope.ServiceProvider
        .GetRequiredService<IDbContextFactory<CharacterDbContext>>().CreateDbContextAsync(CancellationToken.None);
    await using WorldDbContext worldDb = await scope.ServiceProvider
        .GetRequiredService<IDbContextFactory<WorldDbContext>>().CreateDbContextAsync(CancellationToken.None);
    logger.LogInformation("Migrating database if necessary...");
    // Startup migration — host lifetime not active yet, so CancellationToken.None is intentional.
    await authDb.Database.MigrateAsync(CancellationToken.None);
    await characterDb.Database.MigrateAsync(CancellationToken.None);
    await worldDb.Database.MigrateAsync(CancellationToken.None);
}

IEnumerable<IWorkerService> workerServices = app.Services.GetServices<IWorkerService>();
CancellationTokenSource cts = new();
foreach (IWorkerService workerService in workerServices)
{
    logger.LogInformation("Starting worker {Worker}", workerService.GetType().Name);
    await workerService.StartWorker(cts.Token);
}

IReplicatedCache cache = app.Services.GetRequiredService<IReplicatedCache>();
await cache.ConnectAsync();

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
