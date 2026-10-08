using Avalon.Api.Hosting.Middlewares;
using Avalon.Api.Hosting.Worlds;

namespace Avalon.Api.Hosting;

/// <summary>
/// The one request pipeline of every API process (#794, design section 3.1). Its order is fixed here, and
/// PipelineOrderShould pins it: the developer exception page (Development), exception handling, request logging,
/// forwarded headers, routing, CORS, the services' hooks before authentication, authentication, the services' hooks
/// after authentication, the rate limiter, the world routes (when a service declares them), authorization; and the
/// endpoints: <c>/health</c>, <c>/alive</c>, the OpenAPI document and Scalar where they are served (<see cref="ApiDocs"/>),
/// and the controllers.
/// </summary>
public static class ApiPipeline
{
    /// <summary>The origins a browser may call the API from.</summary>
    public static readonly IReadOnlyList<string> CorsOrigins =
    [
        "http://localhost:4200",
        "https://localhost:4200",
        "http://localhost:5210",
        "https://localhost:5210",
        "https://avalon.monster",
        "https://dashboard.avalon.monster",
    ];

    /// <summary>The default endpoints, then the pipeline, on <paramref name="app"/>.</summary>
    public static WebApplication UseAvalonApi(this WebApplication app, IReadOnlyList<IApiService> services)
    {
        app.MapDefaultEndpoints();
        Use(app, app, app.Environment, services);
        return app;
    }

    /// <summary>
    /// The middleware on <paramref name="app"/>, in order, and the OpenAPI, Scalar and controller endpoints on
    /// <paramref name="endpoints"/> (the same application in a host; a test may record the middleware apart).
    /// </summary>
    public static void Use(IApplicationBuilder app, IEndpointRouteBuilder endpoints, IHostEnvironment environment,
        IReadOnlyList<IApiService> services)
    {
        if (environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        // app.UseHsts();
        app.UseMiddleware<ExceptionHandlerMiddleware>();
        app.UseMiddleware<RequestLoggingMiddleware>();

        // Loopback plus the proxies under Application:ForwardedHeaders (#478 review); a header from any
        // other peer is ignored and logged, rate-limited.
        app.UseAvalonForwardedHeaders();

        // In Development, or where Application:ApiDocs:Enabled turns them on; never by default in production (#803).
        if (ApiDocs.AreServed(environment, app.ApplicationServices.GetRequiredService<IConfiguration>()))
            ApiDocs.Map(endpoints);

        app.UseRouting();

        app.UseCors(x => x
            .WithOrigins(CorsOrigins.ToArray())
            .AllowAnyMethod()
            .AllowAnyHeader().AllowCredentials()
        );

        foreach (IApiService service in services)
            service.UseBeforeAuthentication(app);
        app.UseAuthentication();
        foreach (IApiService service in services)
            service.UseAfterAuthentication(app);

        // After authentication, so a signed-in request is counted against its account and a JWT's
        // account has been revalidated first; before the world routes and authorization, so a flood
        // of refused requests is limited too (#561). /health and /alive are exempt.
        app.UseApiRateLimiting();

        // /world/{worldId}/... only (#523): 404 for a world this api does not serve or the caller may
        // not enter, 503 for one whose databases failed at startup. Before authorization, so an unknown
        // world is a 404 whatever the endpoint's role policy.
        if (services.Any(service => service.Needs.WorldRoutes))
            app.UseMiddleware<WorldRouteMiddleware>();

        app.UseAuthorization();

        endpoints.MapControllers();
    }
}
