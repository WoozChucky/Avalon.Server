using System.Net;

namespace Avalon.Network;

public class UdpClientPacket
{
    public EndPoint EndPoint { get; }
    public byte[] Buffer { get; }
    public bool Authenticated { get; }
    
    private readonly Func<ArraySegment<byte>, EndPoint, Task<int>> _responseTask;
    
    public UdpClientPacket(EndPoint endPoint, byte[] buffer, Func<ArraySegment<byte>, EndPoint, Task<int>> responseTask)
    {
        _responseTask = responseTask;
        EndPoint = endPoint ?? throw new ArgumentNullException(nameof(endPoint));
        Buffer = buffer;
        Authenticated = false;
    }
    
    public async Task<int> SendResponseAsync(byte[] buffer)
    {
        return await _responseTask.Invoke(buffer, EndPoint);
    }
}
