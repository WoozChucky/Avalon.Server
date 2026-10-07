using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Avalon.World.GameAuth;

public sealed class WorkloadHttpHandler : DelegatingHandler
{
    private readonly X509Certificate2 _certificate;
    private readonly TimeProvider _clock;
    private long _apiNotBefore;
    private long _apiNotAfter;

    public WorkloadHttpHandler(X509Certificate2 certificate, string apiCertificateSha256, TimeProvider clock)
    {
        var trust = new ApiCertificateTrust(apiCertificateSha256, clock);
        _certificate = certificate;
        _clock = clock;
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            // Deployment explicitly trusts one private API leaf, including hostname, validity,
            // server-auth EKU and key usage. This is not an unconditional TLS validation bypass.
#pragma warning disable MA0039 // Exact certificate pin is the approved private trust anchor.
            ServerCertificateCustomValidationCallback = (_, peer, _, errors) =>
            {
                if (!trust.Validate(peer, errors)) return false;
                // The pinned leaf cannot change on another pooled connection. Check these dates
                // on every send as well: TLS callbacks run only when establishing a connection.
                Interlocked.Exchange(ref _apiNotBefore, peer!.NotBefore.ToUniversalTime().Ticks);
                Interlocked.Exchange(ref _apiNotAfter, peer.NotAfter.ToUniversalTime().Ticks);
                return true;
            }
#pragma warning restore MA0039
        };
        handler.ClientCertificates.Add(certificate);
        InnerHandler = handler;
    }

    private void RequireCurrentApiCertificate()
    {
        long now = _clock.GetUtcNow().UtcDateTime.Ticks;
        long notAfter = Interlocked.Read(ref _apiNotAfter);
        if (notAfter != 0 && (now < Interlocked.Read(ref _apiNotBefore) || now >= notAfter))
            throw new HttpRequestException("The pinned API certificate is outside its validity period.");
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

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _certificate.Dispose();
    }
}
