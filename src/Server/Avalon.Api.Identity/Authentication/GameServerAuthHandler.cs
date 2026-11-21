using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Encodings.Web;
using Avalon.Configuration;
using Avalon.Infrastructure.GameAuth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Avalon.Api.Identity.Authentication;

/// <summary>Only the TLS peer certificate is authority. Player tokens and forwarding/body headers are ignored.</summary>
public sealed class GameServerAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, IOptions<GameWorkloadConfiguration> workloads, TimeProvider clock, IServiceProvider services)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public new const string Scheme = "AvalonGameServer";
    public const string ServerIdClaim = "avalon.game-server-id";
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.IsHttps) return AuthenticateResult.Fail("Workload authentication requires HTTPS.");
        X509Certificate2? certificate = await Context.Connection.GetClientCertificateAsync(Context.RequestAborted);
        if (certificate is null) return AuthenticateResult.Fail("A current TLS client certificate is required.");
        // With no game server configured there is no CA either: nothing can authenticate.
        if (workloads.Value.Servers.Count == 0) return AuthenticateResult.Fail("No game server is configured.");
        ReloadingCertificate ca = services.GetRequiredKeyedService<ReloadingCertificate>(GameWorkloadHosting.CaKey);
        string? refusal = GameWorkloadHosting.RefusalOf(certificate, clock.GetUtcNow().UtcDateTime, ca.Current, workloads.Value);
        if (refusal is not null) return AuthenticateResult.Fail($"The TLS workload certificate was refused: {refusal}.");
        // RefusalOf has established that exactly one configured server ID is named.
        string serverId = AvalonCaTrust.ServerIdOf(certificate)!;
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ServerIdClaim, serverId)], Scheme));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme));
    }
    public static bool IsClientCertificate(X509Certificate2 certificate, DateTime now)
    {
        if (certificate.NotBefore.ToUniversalTime() > now || certificate.NotAfter.ToUniversalTime() <= now) return false;
        return certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().Any(e => e.EnhancedKeyUsages.Cast<Oid>().Any(o => o.Value == "1.3.6.1.5.5.7.3.2"));
    }
}
