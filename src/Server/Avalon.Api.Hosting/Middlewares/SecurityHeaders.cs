namespace Avalon.Api.Hosting.Middlewares;

/// <summary>
/// The transport headers every API response carries: <c>X-Content-Type-Options: nosniff</c> on every response, so a
/// browser renders a body only as the type the API declared, and, outside Development, <c>Strict-Transport-Security</c>
/// on every response to a request that arrived over https (<see cref="AddAvalonHsts"/>).
/// </summary>
public static class SecurityHeaders
{
    /// <summary>
    /// The HSTS policy: one year, the domain that answered only (no <c>includeSubDomains</c>, which would bind every
    /// subdomain of the API's host, the ones this repository does not serve too) and no <c>preload</c>, which is a
    /// commitment to the browsers' lists the edge, not the API, decides on. The edge proxy sends the same policy.
    /// </summary>
    public static IServiceCollection AddAvalonHsts(this IServiceCollection services) =>
        services.AddHsts(hsts =>
        {
            hsts.MaxAge = TimeSpan.FromDays(365);
            hsts.IncludeSubDomains = false;
            hsts.Preload = false;
        });

    /// <summary>Sets <c>X-Content-Type-Options: nosniff</c> on the response before anything else runs.</summary>
    public static IApplicationBuilder UseAvalonSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(static (context, next) =>
        {
            context.Response.Headers.XContentTypeOptions = "nosniff";
            return next(context);
        });
}
