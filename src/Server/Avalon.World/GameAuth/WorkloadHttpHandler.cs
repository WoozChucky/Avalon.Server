using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Avalon.Infrastructure.GameAuth;

namespace Avalon.World.GameAuth;

public sealed class WorkloadHttpHandler : DelegatingHandler
{
    private readonly TimeProvider _clock;
    private long _apiNotBefore;
    private long _apiNotAfter;

    public WorkloadHttpHandler(ReloadingCertificate clientLeaf, ApiCertificateTrust trust, TimeProvider clock)
    {
        _clock = clock;
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            // A renewed client leaf is presented on new connections; recycling bounds how long an old one stays in use.
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            SslOptions =
            {
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                LocalCertificateSelectionCallback = (_, _, _, _, _) => clientLeaf.Current,
                // Trust is the Avalon CA alone, including hostname, validity, server-auth EKU and key usage.
                // This is not an unconditional TLS validation bypass.
#pragma warning disable MA0039 // The Avalon CA is the approved private trust anchor.
                RemoteCertificateValidationCallback = (sender, peer, _, errors) =>
                {
                    // The platform does not report a name mismatch once the chain is untrusted, and the private CA
                    // always is, so the host name is matched here against the DNS names only (no CN, no wildcard).
                    if (peer is null || sender is not SslStream { TargetHostName: { Length: > 0 } host }) return false;
                    using var leaf = new X509Certificate2(peer);
                    if (!leaf.MatchesHostname(host, allowWildcards: false, allowCommonName: false) || !trust.Validate(leaf, errors)) return false;
                    // TLS callbacks run only when a connection is established, so these dates are checked
                    // again on every send as well.
                    Interlocked.Exchange(ref _apiNotBefore, leaf.NotBefore.ToUniversalTime().Ticks);
                    Interlocked.Exchange(ref _apiNotAfter, leaf.NotAfter.ToUniversalTime().Ticks);
                    return true;
                }
#pragma warning restore MA0039
            }
        };
        InnerHandler = handler;
    }

    private void RequireCurrentApiCertificate()
    {
        long now = _clock.GetUtcNow().UtcDateTime.Ticks;
        long notAfter = Interlocked.Read(ref _apiNotAfter);
        if (notAfter != 0 && (now < Interlocked.Read(ref _apiNotBefore) || now >= notAfter))
            throw new HttpRequestException("The API certificate is outside its validity period.");
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequireCurrentApiCertificate();
        return base.SendAsync(request, cancellationToken);
    }

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequireCurrentApiCertificate();
        return base.Send(request, cancellationToken);
    }
}
