using System.Net.Sockets;
using Avalon.Common.Telemetry;
using Avalon.Network.Packets.Abstractions;
using Microsoft.Extensions.Logging;
using ProtoBuf;

namespace Avalon.Network;

public class TcpClient : IRemoteSource
{
    private const string Direction = "OUT";

    private readonly ILogger<TcpClient> _logger;

    public TcpClient(ILoggerFactory loggerFactory, Socket socket, Stream stream)
    {
        _logger = loggerFactory.CreateLogger<TcpClient>();
        Socket = socket ?? throw new ArgumentNullException(nameof(socket));
        Stream = stream ?? throw new ArgumentNullException(nameof(stream));
        Authenticated = false;
    }

    public Socket Socket { get; }
    public Stream Stream { get; }

    public bool Authenticated { get; }

    public bool Connected => Stream.CanWrite;
    public long RoundTripTime => 0;
    public string RemoteAddress => Socket.RemoteEndPoint.ToString();

    public Task SendAsync(NetworkPacket packet)
    {
        if (!Connected)
        {
            throw new IOException("Client is not connected");
        }

        DiagnosticsConfig.World.BytesSent.Add(packet.Size);
        DiagnosticsConfig.World.PacketsSent.Add(1, new KeyValuePair<string, object?>(
            nameof(NetworkPacketType), packet.Header.Type
        ));

        if (packet.Header.Type != NetworkPacketType.SMSG_PLAYER_POSITION_UPDATE)
        {
            _logger.LogDebug("[{Direction}] {PacketType}", Direction, packet.Header.Type);
        }

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
