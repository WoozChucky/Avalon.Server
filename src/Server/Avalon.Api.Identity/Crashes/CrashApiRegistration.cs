using Avalon.Crashes.Queries;
using Avalon.Crashes.Triage;
using Avalon.Database.Crashes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Avalon.Api.Identity.Crashes;

public static class CrashApiRegistration
{
    public static IServiceCollection AddCrashAdminApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CrashApiOptions>(configuration.GetSection("Application:Crashes"));
        services.TryAddSingleton(TimeProvider.System);
        services.AddAuthorization(CrashAdminPolicy.Configure);
        // Hosting registers v1 once. Extend its options without registering a duplicate document.
        services.Configure<OpenApiOptions>("v1", options =>
        {
            options.AddSchemaTransformer<CrashOpenApiTransformer>();
            options.AddOperationTransformer<CrashOpenApiTransformer>();
        });
        // Deliberately lazy: disabled endpoints, startup and OpenAPI never create crash persistence.
        services.AddSingleton<IDbContextFactory<CrashesDbContext>>(_ => new CrashesDbContextFactory(
            new CrashDatabaseOptions { ConnectionString = configuration["Database:Crashes:ConnectionString"] ?? string.Empty }));
        services.AddScoped<ICrashReadStore, CrashReadStore>();
        services.AddScoped<ICrashTriageStore, CrashTriageStore>();
        services.AddSingleton<ICrashSchemaReadiness, CrashSchemaReadiness>();
        services.AddScoped<CrashFeatureGate>();
        services.Configure<ApiBehaviorOptions>(options =>
        {
            Func<ActionContext, IActionResult> original = options.InvalidModelStateResponseFactory;
            options.InvalidModelStateResponseFactory = context => context.HttpContext.Request.Path.StartsWithSegments("/admin/crashes", StringComparison.OrdinalIgnoreCase)
                ? CrashProblemMapper.Invalid() : original(context);
        });
        return services;
    }

    public static IApplicationBuilder UseCrashNoStore(this IApplicationBuilder app) => app.Use((context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/admin/crashes", StringComparison.OrdinalIgnoreCase))
        {
            // OnStarting also covers authorization refusals and outer exception middleware clearing the response.
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.CacheControl = "no-store";
                return Task.CompletedTask;
            });
        }
        return next(context);
    });
}
