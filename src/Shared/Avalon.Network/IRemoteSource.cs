using Avalon.Network.Packets.Abstractions;

namespace Avalon.Network;

public interface IRemoteSource : IDisposable
{
    long RoundTripTime { get; }
    string RemoteAddress { get; }
    Task SendAsync(NetworkPacket packet);
}

public static class Extensions
{
    public static TcpClient AsTcpClient(this IRemoteSource source)
    {
        return (TcpClient) source;
    }
}
