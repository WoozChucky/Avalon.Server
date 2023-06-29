using System.Net.Security;
using System.Net.Sockets;
using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network;

public class TcpClient : IRemoteSource
{
    public long RoundTripTime => 0;
    public string RemoteAddress => Socket.RemoteEndPoint.ToString();

    public Socket Socket { get; }
    public SslStream Stream { get; }

    public bool Authenticated { get; }
    
    public TcpClient(Socket socket, SslStream stream)
    {
        Socket = socket ?? throw new ArgumentNullException(nameof(socket));
        Stream = stream ?? throw new ArgumentNullException(nameof(stream));
        Authenticated = false;
    }

    public Task SendAsync(NetworkPacket packet)
    {
        Serializer.SerializeWithLengthPrefix(Stream, packet, PrefixStyle.Base128);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        Socket.Dispose();
        Stream.Dispose();
    }
}
