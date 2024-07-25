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
