namespace Avalon.Network.Abstractions;

public interface IRemoteSource : IDisposable
{
    long RoundTripTime { get; }
    string RemoteAddress { get; }
    Task SendAsync<T>(T packet) where T : class;
}

public static class Extensions
{
    public static TcpClient AsTcpClient(this IRemoteSource source)
    {
        return (TcpClient) source;
    }
    
    public static UdpClientPacket AsUdpClient(this IRemoteSource source)
    {
        return (UdpClientPacket) source;
    }
}
