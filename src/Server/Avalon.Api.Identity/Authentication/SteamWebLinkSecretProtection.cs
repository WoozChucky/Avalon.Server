using Avalon.Api.Hosting.Middlewares;
using OpenTelemetry.Instrumentation.AspNetCore;

namespace Avalon.Api.Identity.Authentication;

public static class SteamWebLinkSecretProtection
{
    public static void AddSteamWebLinkSecretProtection(this IServiceCollection services)
    {
        // The callback's query carries the provider's signed assertion: the request log keeps its path only.
        services.Configure<RequestLoggingOptions>(options => options.HideQueryString.Add(SteamOpenIdCallbackMiddleware.IsCallback));
        services.PostConfigure<AspNetCoreTraceInstrumentationOptions>(options =>
        {
            Func<HttpContext, bool>? previous = options.Filter;
            options.Filter = context => !SteamOpenIdCallbackMiddleware.IsCallback(context.Request.Path) && (previous?.Invoke(context) ?? true);
        });
        // Hosting logs contain the raw request target before any middleware can redact it.
        // Enforce these rules after configuration, including provider-specific and more specific categories.
        services.PostConfigure<LoggerFilterOptions>(options =>
        {
            string?[] providers = options.Rules.Select(r => r.ProviderName).Append(null).Append("Serilog").Append("OpenTelemetry")
                .Append("Serilog.Extensions.Logging.SerilogLoggerProvider").Append("OpenTelemetry.Logs.OpenTelemetryLoggerProvider").Distinct().ToArray();
            string[] prefixes = new[] { "Microsoft.AspNetCore.Hosting.Diagnostics", "AspNet.Security.OpenId" };
            string?[] categories = options.Rules.Select(r => r.CategoryName).Where(c => c is not null && prefixes.Any(p => c.StartsWith(p, StringComparison.Ordinal)))
                .Concat(prefixes).Distinct().ToArray();
            foreach (string? provider in providers)
                foreach (string? category in categories) options.Rules.Add(new(provider, category, LogLevel.None, null));
        });
    }
}
