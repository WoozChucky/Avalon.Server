using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Encodings.Web;
using Avalon.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Avalon.Api.Authentication;

/// <summary>Only the TLS peer certificate is authority. Player tokens and forwarding/body headers are ignored.</summary>
public sealed class GameServerAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, IOptions<GameWorkloadConfiguration> workloads, TimeProvider clock)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public new const string Scheme = "AvalonGameServer";
    public const string ServerIdClaim = "avalon.game-server-id";
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.IsHttps) return AuthenticateResult.Fail("Workload authentication requires HTTPS.");
        var certificate = await Context.Connection.GetClientCertificateAsync(Context.RequestAborted);
        if (certificate is null || !IsClientCertificate(certificate, clock.GetUtcNow().UtcDateTime))
            return AuthenticateResult.Fail("A current TLS client certificate is required.");
        var pin = Convert.ToHexString(SHA256.HashData(certificate.RawData));
        var definition = workloads.Value.Servers.SingleOrDefault(s => string.Equals(s.ClientCertificateSha256, pin, StringComparison.OrdinalIgnoreCase));
        if (definition is null) return AuthenticateResult.Fail("The TLS workload certificate is not assigned.");
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ServerIdClaim, definition.ServerId)], Scheme));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme));
    }
    public static bool IsClientCertificate(X509Certificate2 certificate, DateTime now)
    {
        if (certificate.NotBefore.ToUniversalTime() > now || certificate.NotAfter.ToUniversalTime() <= now) return false;
        return certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().Any(e => e.EnhancedKeyUsages.Cast<Oid>().Any(o => o.Value == "1.3.6.1.5.5.7.3.2"));
    }
}
