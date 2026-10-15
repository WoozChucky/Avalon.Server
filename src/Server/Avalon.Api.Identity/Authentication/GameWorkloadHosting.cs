using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Avalon.Configuration;
using Microsoft.AspNetCore.Server.Kestrel.Https;

namespace Avalon.Api.Identity.Authentication;

/// <summary>Direct TLS workload listener; certificate forwarding is deliberately not installed.</summary>
public static class GameWorkloadHosting
{
    public const string EndpointName = "GameInternal";

    /// <summary>The workload listener's port when <c>Kestrel:Endpoints:GameInternal:Url</c> names none: the chart's.</summary>
    public const int DefaultPort = 9443;

    /// <summary>The port of the workload listener <c>Kestrel:Endpoints:GameInternal:Url</c> configures, or <see cref="DefaultPort"/>.</summary>
    public static int PortOf(IConfiguration configuration) =>
        Uri.TryCreate(configuration[$"Kestrel:Endpoints:{EndpointName}:Url"], UriKind.Absolute, out Uri? url) && url.Port > 0
            ? url.Port
            : DefaultPort;

    public static void ConfigureGameWorkloadListener(this WebApplicationBuilder builder)
    {
        GameWorkloadConfiguration configuration = builder.Configuration.GetSection("Application:GameWorkloads").Get<GameWorkloadConfiguration>() ?? new();
        configuration.Validate();
        if (configuration.Servers.Count == 0) return; // No assigned server can allocate or authenticate.
        string? url = builder.Configuration[$"Kestrel:Endpoints:{EndpointName}:Url"];
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? endpoint) || endpoint.Scheme != "https")
            throw new InvalidOperationException($"Kestrel:Endpoints:{EndpointName}:Url must configure a direct HTTPS workload listener.");
        builder.WebHost.ConfigureKestrel(options => options.Configure(builder.Configuration.GetSection("Kestrel"), reloadOnChange: false)
            .Endpoint(EndpointName, listener =>
            {
                if (!listener.IsHttps) throw new InvalidOperationException("The game workload listener must use HTTPS.");
                listener.HttpsOptions.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                // The pinned leaf is the deployment trust anchor, including for a private/self-signed leaf.
                // TLS verifies possession of its private key; each HTTP request repeats expiry/EKU/pin checks.
                listener.HttpsOptions.ClientCertificateValidation = (certificate, _, _) =>
                {
                    IServiceProvider? services = options.ApplicationServices;
                    DateTime now = (services?.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow().UtcDateTime;
                    string pin = Convert.ToHexString(SHA256.HashData(certificate.RawData));
                    string? refusal = RefusalOf(certificate, pin, now, configuration);
                    // Kestrel logs a refused handshake at Debug only (#1051): say why, rate-limited, with the pin alone.
                    if (refusal is not null) services?.GetService<RefusedWorkloadCertificateLog>()?.Refused(refusal, pin);
                    return refusal is null;
                };
            }));
    }

    /// <summary>
    /// Why the workload listener refuses <paramref name="certificate"/>, whose SHA-256 is <paramref name="pin"/>, at
    /// <paramref name="now"/>: outside its validity period, without the client authentication usage, or assigned to no
    /// game server. Null when it accepts it.
    /// </summary>
    public static string? RefusalOf(X509Certificate2 certificate, string pin, DateTime now, GameWorkloadConfiguration configuration)
    {
        if (certificate.NotBefore.ToUniversalTime() > now || certificate.NotAfter.ToUniversalTime() <= now)
            return "outside its validity period";
        if (!GameServerAuthHandler.IsClientCertificate(certificate, now))
            return "without the client authentication usage";
        return configuration.Servers.Any(s => string.Equals(s.ClientCertificateSha256, pin, StringComparison.OrdinalIgnoreCase))
            ? null
            : "assigned to no game server";
    }
}
