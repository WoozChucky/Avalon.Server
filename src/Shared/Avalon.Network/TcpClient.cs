using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication.ExtendedProtection;
using Avalon.Common.Telemetry;
using Avalon.Network.Packets.Abstractions;
using Microsoft.Extensions.Logging;
using ProtoBuf;

namespace Avalon.Network;

public class TcpClient : IRemoteSource
{
    public long RoundTripTime => 0;
    public string RemoteAddress => Socket.RemoteEndPoint.ToString();

    public Socket Socket { get; }
    public Stream Stream { get; }

    public bool Authenticated { get; }
    
    public bool Connected => Stream.CanWrite;
    
    private readonly ILogger<TcpClient> _logger;
    
    private const string Direction = "OUT";
    
    public TcpClient(ILoggerFactory loggerFactory, Socket socket, Stream stream)
    {
        _logger = loggerFactory.CreateLogger<TcpClient>();
        Socket = socket ?? throw new ArgumentNullException(nameof(socket));
        Stream = stream ?? throw new ArgumentNullException(nameof(stream));
        Authenticated = false;
    }

    public Task SendAsync(NetworkPacket packet)
    {
        if (!Connected)
        {
            throw new IOException("Client is not connected");
        }
        
        DiagnosticsConfig.Server.BytesSent.Add(packet.Size);
        DiagnosticsConfig.Server.PacketsSent.Add(1, new KeyValuePair<string, object?>(
            nameof(NetworkPacketType), packet.Header.Type
        ));
        
        if (packet.Header.Type != NetworkPacketType.SMSG_NPC_UPDATE)
            _logger.LogDebug("[{Direction}] {PacketType}", Direction, packet.Header.Type);
        
        Serializer.SerializeWithLengthPrefix(Stream, packet, PrefixStyle.Base128);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        Socket.Shutdown(SocketShutdown.Both);
        Socket.Dispose();
        Stream.Dispose();
    }
}
