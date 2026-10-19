using Avalon.Common.Telemetry;
using Avalon.Infrastructure.Login;

namespace Avalon.Api.Identity.Services;

/// <summary>One bounded metric and structured outcome log per password or MFA login decision.</summary>
internal static class LoginTelemetry
{
    public static void Record(ILogger logger, string method, string result, LoginSource source)
    {
        DiagnosticsConfig.Api.Logins.Add(1,
            new KeyValuePair<string, object?>("result", result),
            new KeyValuePair<string, object?>("method", method));

        LogLevel level = result == "rate_limited" ? LogLevel.Debug : LogLevel.Information;
        if (logger.IsEnabled(level))
            logger.Log(level, "Login {LoginMethod}: {LoginResult} from {ClientAddress}", method, result, RemoteAddress.SourceOf(source.Ip));
    }
}
