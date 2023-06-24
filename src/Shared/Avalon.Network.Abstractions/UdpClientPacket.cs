using System.Net;
using System.Net.Sockets;
using ProtoBuf;

namespace Avalon.Network.Abstractions;

public class UdpClientPacket : IRemoteSource
{
    public string RemoteAddress => EndPoint.ToString();
    public EndPoint EndPoint { get; }
    public byte[] Buffer { get; }
    public bool Authenticated { get; }
    
    private readonly Func<ArraySegment<byte>, SocketFlags, EndPoint, Task<int>> _responseTask;
    
    public UdpClientPacket(EndPoint endPoint, byte[] buffer, Func<ArraySegment<byte>, SocketFlags, EndPoint, Task<int>> responseTask)
    {
        _responseTask = responseTask;
        EndPoint = endPoint ?? throw new ArgumentNullException(nameof(endPoint));
        Buffer = buffer;
        Authenticated = false;
    }
    
    public async Task<int> SendResponseAsync(byte[] buffer)
    {
        try
        {
            return await _responseTask.Invoke(buffer, SocketFlags.None, EndPoint);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return 0;
        }
        
    }

    public async Task SendAsync<T>(T packet) where T : class
    {
        await using var stream = new MemoryStream();
        Serializer.SerializeWithLengthPrefix(stream, packet, PrefixStyle.Base128);
        await _responseTask.Invoke(stream.ToArray(), SocketFlags.None, EndPoint);
    }

    public void Dispose()
    {
    }
}
