using System.Reflection;
using Avalon.Network.Abstractions;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Deserialization;
using Avalon.Network.Packets.Serialization;
using Microsoft.Extensions.Logging;

namespace Avalon.Game;

public interface IAvalonGame
{
    Task HandleServerVersionPacket(IRemoteSource source, NetworkPacket packet);
}

public class AvalonGame : IAvalonGame
{
    private readonly ILogger<AvalonGame> _logger;
    private readonly CancellationTokenSource _cts;
    private readonly IPacketDeserializer _packetDeserializer;
    private readonly IPacketSerializer _packetSerializer;

    public AvalonGame(ILogger<AvalonGame> logger, CancellationTokenSource cts, IPacketDeserializer packetDeserializer, IPacketSerializer packetSerializer)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cts = cts ?? throw new ArgumentNullException(nameof(cts));
        _packetDeserializer = packetDeserializer;
        _packetSerializer = packetSerializer;
    }

    public async Task HandleServerVersionPacket(IRemoteSource source, NetworkPacket packet)
    {
        var client = (TcpClient) source;
        
        _logger.LogDebug("Handling server version packet from {EndPoint}", client.Socket.RemoteEndPoint);
        
        var result = SServerVersionPacket.Create(
            Assembly.GetExecutingAssembly().GetName().Version?.Major ?? 0,
            Assembly.GetExecutingAssembly().GetName().Version?.Minor ?? 0,
            Assembly.GetExecutingAssembly().GetName().Version?.Build ?? 0,
            Assembly.GetExecutingAssembly().GetName().Version?.Revision ?? 0
        );
        
        await _packetSerializer.SerializeToNetwork(client.Stream, result);
    }
}
