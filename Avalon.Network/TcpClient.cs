using System.Net.Security;
using System.Net.Sockets;

namespace Avalon.Network;

public class TcpClient : IRemoteSource
{
    public Socket Socket { get; }
    public SslStream Stream { get; }

    public bool Authenticated { get; }
    
    public TcpClient(Socket socket, SslStream stream)
    {
        Socket = socket ?? throw new ArgumentNullException(nameof(socket));
        Stream = stream ?? throw new ArgumentNullException(nameof(stream));
        Authenticated = false;
    }
}
