using Microsoft.Extensions.Logging;
using OpenTelemetry.Instrumentation.AspNetCore;

namespace Avalon.Api.Authentication;

public static class SteamWebLinkSecretProtection
{
    public static void AddSteamWebLinkSecretProtection(this IServiceCollection services)
    {
        services.PostConfigure<AspNetCoreTraceInstrumentationOptions>(options =>
        {
            var previous = options.Filter;
            options.Filter = context => !SteamOpenIdCallbackMiddleware.IsCallback(context.Request.Path) && (previous?.Invoke(context) ?? true);
        });
        // Hosting logs contain the raw request target before any middleware can redact it.
        // Enforce these rules after configuration, including provider-specific and more specific categories.
        services.PostConfigure<LoggerFilterOptions>(options =>
        {
            var providers = options.Rules.Select(r => r.ProviderName).Append(null).Append("Serilog").Append("OpenTelemetry")
                .Append("Serilog.Extensions.Logging.SerilogLoggerProvider").Append("OpenTelemetry.Logs.OpenTelemetryLoggerProvider").Distinct().ToArray();
            var prefixes = new[] { "Microsoft.AspNetCore.Hosting.Diagnostics", "AspNet.Security.OpenId" };
            var categories = options.Rules.Select(r => r.CategoryName).Where(c => c is not null && prefixes.Any(p => c.StartsWith(p, StringComparison.Ordinal)))
                .Concat(prefixes).Distinct().ToArray();
            foreach (var provider in providers)
                foreach (var category in categories) options.Rules.Add(new(provider, category, LogLevel.None, null));
        });
    }
}
