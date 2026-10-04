using System.Security.Cryptography.X509Certificates;

namespace Avalon.World.GameAuth;

public sealed class WorkloadHttpHandler : DelegatingHandler
{
    private readonly X509Certificate2 _certificate;
    public WorkloadHttpHandler(X509Certificate2 certificate)
    {
        _certificate = certificate;
        var handler = new HttpClientHandler { AllowAutoRedirect = false, CheckCertificateRevocationList = true };
        handler.ClientCertificates.Add(certificate);
        InnerHandler = handler;
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _certificate.Dispose();
    }
}
