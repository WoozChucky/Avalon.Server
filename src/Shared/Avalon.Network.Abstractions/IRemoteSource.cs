namespace Avalon.Network.Abstractions;

public interface IRemoteSource
{
    
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
