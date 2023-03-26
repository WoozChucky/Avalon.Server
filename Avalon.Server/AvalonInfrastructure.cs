using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;


namespace Avalon.Server;

public class AvalonInfrastructure : IDisposable
{
    private readonly X509Certificate2 _serverCertificate;
    private readonly Socket _serverTcp;
    private readonly CancellationTokenSource _cts;

    private volatile bool _isRunning;
    
    public AvalonInfrastructure()
    {
        _cts = new CancellationTokenSource();
        _serverTcp = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _serverTcp.Bind(new IPEndPoint(IPAddress.Any, 21000));
        var serverCertBytes = File.ReadAllBytesAsync("cert-tcp.pfx").ConfigureAwait(true).GetAwaiter().GetResult();
        _serverCertificate = new X509Certificate2(serverCertBytes, "avalon");
    }
    
    public async Task Run(bool block = true)
    {
        _serverTcp.Listen(10);
        _isRunning = true;
        
        if (block)
        {
            await InternalServerLoop();
        }
        else
        {
            await Task.Factory.StartNew(InternalServerLoop).ConfigureAwait(false);
        }
    }
    
    public void Stop()
    {
        _cts.Cancel();
        _isRunning = false;
        _serverTcp.Dispose();
    }
    
    private async Task InternalServerLoop()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var client = await _serverTcp.AcceptAsync(_cts.Token);
                await HandleNewConnection(client).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException e)
        {
            //TODO: Gracefully exit the loop when cancellation is requested
        }
    }

    public bool IsRunning => _isRunning;

    private async Task HandleNewConnection(Socket socket)
    {
        var sslStream = new SslStream(new NetworkStream(socket), false, OnClientCertificateValidation);
        try
        {
            await sslStream.AuthenticateAsServerAsync(_serverCertificate, true, SslProtocols.Tls12, true);

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
    
    private static bool OnClientCertificateValidation(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslpolicyerrors)
    {
        //TODO(Nuno): Implement proper certificate validation
        return true;
    }

    private void ReleaseUnmanagedResources()
    {
        // TODO release unmanaged resources here
    }

    private void Dispose(bool disposing)
    {
        if (disposing)
        {
            _serverCertificate.Dispose();
            _serverTcp.Dispose();
            _cts.Dispose();
        }
        ReleaseUnmanagedResources();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~AvalonInfrastructure()
    {
        Dispose(false);
    }
}
