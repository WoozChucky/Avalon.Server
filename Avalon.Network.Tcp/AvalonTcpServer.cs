using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Avalon.Network.Tcp.Configuration;
using Microsoft.Extensions.Logging;

namespace Avalon.Network.Tcp;

public class AvalonTcpServer : IAvalonTcpServer
{
    private readonly ILogger<AvalonTcpServer> _logger;
    private readonly AvalonTcpServerConfiguration _configuration;
    private readonly CancellationTokenSource _cts;
    private readonly X509Certificate2 _certificate;
    private readonly Socket _socket;
    
    private volatile bool _isRunning;
    
    public event TcpClientConnectedHandler ClientConnected;

    public AvalonTcpServer(ILogger<AvalonTcpServer> logger, AvalonTcpServerConfiguration configuration, CancellationTokenSource cts)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _cts = cts ?? throw new ArgumentNullException(nameof(cts));
        _isRunning = false;
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _socket.Bind(new IPEndPoint(IPAddress.Any, _configuration.ListenPort));
        var serverCertBytes = File.ReadAllBytesAsync(
                _configuration.CertificatePath ?? throw new ArgumentNullException(nameof(configuration.CertificatePath))
                ).ConfigureAwait(true).GetAwaiter().GetResult();
        _certificate = new X509Certificate2(serverCertBytes, _configuration.CertificatePassword);
    }
    
    ~AvalonTcpServer()
    {
        Dispose(false);
    }
    
    public bool IsRunning => _isRunning;
    
    public async Task RunAsync()
    {
        if (_isRunning)
        {
            throw new InvalidOperationException("Server is already running.");
        }
        
        _socket.Listen(_configuration.Backlog);
        _isRunning = true;
        
        _logger.LogInformation("Server started at {EndPoint}", _socket.LocalEndPoint);
        
#pragma warning disable CS4014
        Task.Factory.StartNew(InternalServerLoop, _cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
#pragma warning restore CS4014
    }

    public Task StopAsync()
    {
        if (!_isRunning) throw new InvalidOperationException("Server is not running.");

        _isRunning = false;

        //TODO(Nuno): Close all connections.
        //TODO(Nuno): Send the connection disconnect packet to all clients.
        _logger.LogInformation("Server stopped");
        
        return Task.CompletedTask;
    }
    
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
        _logger.LogDebug("Disposed AvalonTcpServer");
    }

    private async Task InternalServerLoop()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var client = await _socket.AcceptAsync(_cts.Token);
                await HandleNewConnection(client).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Server loop cancelled");
        }
    }

    private async Task HandleNewConnection(Socket client)
    {
        var sslStream = new SslStream(new NetworkStream(client), false, OnClientCertificateValidation);
        try
        {
            await sslStream.AuthenticateAsServerAsync(_certificate, true, SslProtocols.Tls12, true);
            
            ClientConnected?.Invoke(this, new TcpClient(client, sslStream));
        }
        catch (AuthenticationException e)
        {
            _logger.LogWarning(e, "Failed to authenticate: {Message}", e.Message);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failed to handle new connection: {Message}", e.Message);
        }
    }

    private bool OnClientCertificateValidation(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslpolicyerrors)
    {
        
        if (certificate == null)
        {
            _logger.LogWarning("Client certificate is null");
            return false;
        }
        
        var expiryDate = DateTime.Parse(certificate.GetExpirationDateString());
        
        if (expiryDate < DateTime.UtcNow)
        {
            _logger.LogWarning("Client certificate is expired");
            return false;
        }

        if (!certificate.GetSerialNumberString().Equals(_certificate.GetSerialNumberString()))
        {
            _logger.LogWarning("Client certificate serial number does not match server certificate serial number");
            return false;
        }
        
        if (!certificate.Issuer.Equals(_certificate.Issuer))
        {
            _logger.LogWarning("Client certificate issuer does not match server certificate issuer");
            return false;
        }
        
        if (!certificate.Subject.Equals(_certificate.Subject))
        {
            _logger.LogWarning("Client certificate subject does not match server certificate subject");
            return false;
        }

        //TODO(Nuno): Make this a configuration variable to enable or disable this checks.
        if (false) {
            // Check if the client certificate chain is valid
            if (chain != null && chain.ChainStatus.Any(o => o.Status != X509ChainStatusFlags.UntrustedRoot))
            {
                _logger.LogWarning("Client certificate chain is invalid");
                return false;
            }

            // Check if the SSL policy errors indicate a problem
            if (sslpolicyerrors != SslPolicyErrors.None)
            {
                _logger.LogWarning("SSL policy errors encountered during client certificate validation: {SslPolicyErrors}", sslpolicyerrors);
                return false;
            }
        }
        
        return true;
    }

    private void Dispose(bool disposing)
    {
        if (disposing)
        {
            _certificate.Dispose();
            _logger.LogTrace("Disposed Certificate");
            _socket.Dispose();
            _logger.LogTrace("Disposed Socket");
        }
    }
}
