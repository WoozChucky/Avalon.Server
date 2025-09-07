using System.Text.Json;
using System.Text.Json.Serialization;
using Avalon.Api;
using Avalon.Api.Config;
using Avalon.Api.Contract.Mappers;
using Avalon.Api.Converters;
using Avalon.Api.Middlewares;
using Avalon.Api.Services;
using Avalon.Database.Auth;
using Avalon.Database.Character;
using Avalon.Database.World;
using Avalon.Hosting.Extensions;
using Avalon.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

IConfigurationRoot configuration = builder
    .Configuration
    .AddJsonFile("appsettings.json", true, false)
    .AddEnvironmentVariables().Build();

builder.AddServiceDefaults();

IServiceCollection services = builder.Services;

services.AddCustomLogging(configuration);

// Add services to the container.
{
    ApplicationConfig applicationConfig = new();
    configuration.Bind("Application", applicationConfig);
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
            options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            options.JsonSerializerOptions.Converters.Add(new ValueObjectJsonConverterFactory());
        });

    services.AddAuth(applicationConfig);
    services.AddSwagger();
    services.AddInfrastructure(applicationConfig);
    services.AddAutoMapper(action =>
    {
        action.AddProfile<AutoMappingProfile>();
    });
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

    app.UseForwardedHeaders(new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
    });

    app.UseSwagger();
    app.UseSwaggerUI();

    app.UseRouting();

    app.UseCors(x => x
        .WithOrigins("http://localhost:4200", "https://avalon.monster", "https://dashboard.avalon.monster")
        .AllowAnyMethod()
        .AllowAnyHeader()
    );

    app.UseAuthentication();

    app.UseAuthorization();

    app.MapControllers();
}

ILogger<Program> logger = app.Services.GetRequiredService<ILogger<Program>>();

await using (AsyncServiceScope scope = app.Services.CreateAsyncScope())
{
    AuthDbContext authDb = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
    CharacterDbContext characterDb = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();
    WorldDbContext worldDb = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
    logger.LogInformation("Migrating database if necessary...");
    await authDb.Database.MigrateAsync();
    await characterDb.Database.MigrateAsync();
    await worldDb.Database.MigrateAsync();
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
AppDomain.CurrentDomain.ProcessExit += (_, _) =>
{
    cts.Cancel();
    logger.LogInformation("Exited successfully");
};
Console.CancelKeyPress += (_, _) => { logger.LogInformation("Ctrl+C was pressed, stopping application..."); };

await app.RunAsync();
