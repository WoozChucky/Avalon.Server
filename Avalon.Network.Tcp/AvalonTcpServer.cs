using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Avalon.Network.Tcp.Configuration;

namespace Avalon.Network.Tcp;

public class AvalonTcpServer : IAvalonNetworkServer
{
    private readonly AvalonTcpServerConfiguration _configuration;
    private readonly X509Certificate2 _certificate;
    private readonly Socket _socket;
    
    private volatile CancellationTokenSource _cts;
    private volatile bool _isRunning;

    public AvalonTcpServer(AvalonTcpServerConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _cts = new CancellationTokenSource();
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
    
    public async Task RunAsync(bool blocking = true)
    {
        if (_isRunning)
        {
            throw new InvalidOperationException("Server is already running.");
        }
        
        _cts = _cts.Token.IsCancellationRequested ? new CancellationTokenSource() : _cts;
        _socket.Listen(_configuration.Backlog);
        _isRunning = true;
        
        if (blocking)
        {
            await InternalServerLoop();
        }
        else
        {
            await Task.Factory.StartNew(InternalServerLoop).ConfigureAwait(false);
        }
    }

    public Task StopAsync()
    {
        if (!_isRunning)
        {
            throw new InvalidOperationException("Server is not running.");
        }

        _isRunning = false;
        _cts.Cancel();

        //TODO(Nuno): Close all connections.
        //TODO(Nuno): Send the connection disconnect packet to all clients.
        
        return Task.CompletedTask;
    }
    
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
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
        catch (OperationCanceledException e)
        {
            //TODO: Gracefully exit the loop when cancellation is requested
        }
    }

    private async Task HandleNewConnection(Socket client)
    {
        var sslStream = new SslStream(new NetworkStream(client), false, OnClientCertificateValidation);
        try
        {
            await sslStream.AuthenticateAsServerAsync(_certificate, true, SslProtocols.Tls12, true);

            // Receive a response from the server
            var buffer = new byte[1024];
            var bytesRead = await sslStream.ReadAsync(buffer, _cts.Token);
            if (bytesRead == 0)
            {
                // Remote client disconnected
                return;
            }

            var response = Encoding.UTF8.GetString(buffer, 0, bytesRead);

            Console.WriteLine(response);
        }
        catch (AuthenticationException e)
        {
            Console.WriteLine($"Failed to authenticate: {e.Message}");
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failed to communicate: {e.Message}");
        }
        finally
        {
            await sslStream.DisposeAsync();
        }
    }

    private bool OnClientCertificateValidation(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslpolicyerrors)
    {
        return true;
    }

    private void Dispose(bool disposing)
    {
        if (disposing)
        {
            _certificate.Dispose();
            _socket.Dispose();
            _cts.Dispose();
        }
    }
}