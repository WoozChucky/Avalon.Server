using System.Security.Cryptography;
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
                    GameServerAuthHandler.IsClientCertificate(certificate, DateTime.UtcNow) &&
                    configuration.Servers.Any(s => string.Equals(s.ClientCertificateSha256,
                        Convert.ToHexString(SHA256.HashData(certificate.RawData)), StringComparison.OrdinalIgnoreCase));
            }));
    }
}
