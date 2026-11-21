using System.Security.Cryptography.X509Certificates;
using Avalon.Configuration;
using Avalon.Infrastructure.GameAuth;
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

    /// <summary>Keyed-service key of the <see cref="ReloadingCertificate"/> the workload listener presents as its server certificate.</summary>
    public const string LeafKey = "game-workload-leaf";

    /// <summary>Keyed-service key of the <see cref="ReloadingCertificate"/> holding the Avalon CA a world's client certificate must chain to.</summary>
    public const string CaKey = "game-workload-ca";

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
                IServiceProvider services = options.ApplicationServices;
                // Resolved here, when the listener starts, so an unreadable certificate refuses startup.
                ReloadingCertificate leaf = services.GetRequiredKeyedService<ReloadingCertificate>(LeafKey);
                ReloadingCertificate ca = services.GetRequiredKeyedService<ReloadingCertificate>(CaKey);
                TimeProvider clock = services.GetService<TimeProvider>() ?? TimeProvider.System;
                listener.HttpsOptions.ServerCertificateSelector = (_, _) => leaf.Current;
                listener.HttpsOptions.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                // The Avalon CA is the only trust anchor. TLS verifies possession of the leaf's private key; each HTTP
                // request repeats the validity, usage, CA and identity checks against the current CA.
                listener.HttpsOptions.ClientCertificateValidation = (certificate, _, _) =>
                {
                    string? refusal = RefusalOf(certificate, clock.GetUtcNow().UtcDateTime, ca.Current, configuration);
                    // Kestrel logs a refused handshake at Debug only (#1051): say why, rate-limited.
                    if (refusal is not null)
                    {
                        string? serverId = AvalonCaTrust.ServerIdOf(certificate);
                        services.GetService<RefusedWorkloadCertificateLog>()?.Refused(refusal, certificate.Subject, serverId, IsAssigned(serverId, configuration));
                    }
                    return refusal is null;
                };
            }));
    }

    /// <summary>
    /// Why the workload listener refuses <paramref name="certificate"/> at <paramref name="now"/>: outside its validity
    /// period, without the client authentication usage, not issued by <paramref name="ca"/>, naming no game server (no
    /// single <c>urn:avalon:server:</c> URI), or naming one no configured game server has. Null when it accepts it.
    /// </summary>
    public static string? RefusalOf(X509Certificate2 certificate, DateTime now, X509Certificate2 ca, GameWorkloadConfiguration configuration)
    {
        if (certificate.NotBefore.ToUniversalTime() > now || certificate.NotAfter.ToUniversalTime() <= now)
            return "outside its validity period";
        if (!GameServerAuthHandler.IsClientCertificate(certificate, now))
            return "without the client authentication usage";
        if (!AvalonCaTrust.Chains(certificate, ca, now))
            return "not issued by the Avalon CA";
        string? serverId = AvalonCaTrust.ServerIdOf(certificate);
        if (serverId is null)
            return "naming no game server";
        return IsAssigned(serverId, configuration) ? null : "assigned to no game server";
    }

    /// <summary>Whether a configured game server has the ID <paramref name="serverId"/>.</summary>
    public static bool IsAssigned(string? serverId, GameWorkloadConfiguration configuration) =>
        serverId is not null && configuration.Servers.Any(s => string.Equals(s.ServerId, serverId, StringComparison.Ordinal));
}
