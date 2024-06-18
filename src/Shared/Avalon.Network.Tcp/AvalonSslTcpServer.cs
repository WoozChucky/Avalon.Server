using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Avalon.Network.Tcp.Configuration;
using Microsoft.Extensions.Logging;

namespace Avalon.Network.Tcp;

public class AvalonSslTcpServer : AvalonTcpServer, IAvalonTcpServer
{
    private readonly X509Certificate2 _certificate;
    
    public new event TcpClientConnectedHandler? ClientConnected;
    
    public AvalonSslTcpServer(ILoggerFactory loggerFactory, AvalonTcpServerConfiguration configuration, CancellationTokenSource cts) : base(loggerFactory, configuration, cts)
    {
        var serverCertBytes = File.ReadAllBytesAsync(
            Configuration.CertificatePath ?? throw new ArgumentNullException(nameof(configuration.CertificatePath))
        ).ConfigureAwait(true).GetAwaiter().GetResult();
        _certificate = new X509Certificate2(serverCertBytes, Configuration.CertificatePassword);
    }
    
    protected override async Task HandleNewConnection(Socket client)
    {
        var sslStream = new SslStream(new NetworkStream(client), false, OnClientCertificateValidation);
        try
        {
            await sslStream.AuthenticateAsServerAsync(_certificate, false, SslProtocols.Tls12, false).ConfigureAwait(true);
            
            ClientConnected?.Invoke(this, new TcpClient(LoggerFactory, client, sslStream));
        }
        catch (AuthenticationException e)
        {
            Logger.LogWarning(e, "Failed to authenticate: {Message}", e.Message);
        }
        catch (Exception e)
        {
            Logger.LogWarning(e, "Failed to handle new connection: {Message}", e.Message);
        }
    }
    
    private bool OnClientCertificateValidation(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslpolicyerrors)
    {
        return true;
        if (certificate == null)
        {
            Logger.LogWarning("Client certificate is null");
            return false;
        }
        
        var expiryDate = DateTime.Parse(certificate.GetExpirationDateString());
        
        if (expiryDate < DateTime.UtcNow)
        {
            Logger.LogWarning("Client certificate is expired");
            return false;
        }

        if (!certificate.GetSerialNumberString().Equals(_certificate.GetSerialNumberString()))
        {
            Logger.LogWarning("Client certificate serial number does not match server certificate serial number");
            return false;
        }
        
        if (!certificate.Issuer.Equals(_certificate.Issuer))
        {
            Logger.LogWarning("Client certificate issuer does not match server certificate issuer");
            return false;
        }
        
        if (!certificate.Subject.Equals(_certificate.Subject))
        {
            Logger.LogWarning("Client certificate subject does not match server certificate subject");
            return false;
        }

        //TODO(Nuno): Make this a configuration variable to enable or disable this checks.
        if (false) {
            // Check if the client certificate chain is valid
            if (chain != null && chain.ChainStatus.Any(o => o.Status != X509ChainStatusFlags.UntrustedRoot))
            {
                Logger.LogWarning("Client certificate chain is invalid");
                return false;
            }

            // Check if the SSL policy errors indicate a problem
            if (sslpolicyerrors != SslPolicyErrors.None)
            {
                Logger.LogWarning("SSL policy errors encountered during client certificate validation: {SslPolicyErrors}", sslpolicyerrors);
                return false;
            }
        }
        
        return true;
    }
    
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _certificate.Dispose();
            Logger.LogTrace("Disposed Certificate");
        }
    }
    
}
