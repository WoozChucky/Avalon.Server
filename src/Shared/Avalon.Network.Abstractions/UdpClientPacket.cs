using System.Net;
using System.Net.Sockets;
using ENet;
using ProtoBuf;

namespace Avalon.Network.Abstractions;

public class UdpClientPacket : IRemoteSource
{
    public long RoundTripTime => _peer.RoundTripTime;
    public string RemoteAddress => $"{_peer.IP}:{_peer.Port}";

    public byte[] Buffer { get; }
    public bool Authenticated { get; }
    
    private Peer _peer;
    
    /*
    private readonly Func<ArraySegment<byte>, SocketFlags, EndPoint, Task<int>> _responseTask;
    
    
    public UdpClientPacket(EndPoint endPoint, byte[] buffer, Func<ArraySegment<byte>, SocketFlags, EndPoint, Task<int>> responseTask)
    {
        _responseTask = responseTask;
        EndPoint = endPoint ?? throw new ArgumentNullException(nameof(endPoint));
        Buffer = buffer;
        Authenticated = false;
    }
    
    public async Task SendAsync<T>(T packet) where T : class
    {
        await using var stream = new MemoryStream();
        Serializer.SerializeWithLengthPrefix(stream, packet, PrefixStyle.Base128);
        await _responseTask.Invoke(stream.ToArray(), SocketFlags.None, EndPoint);
    }
    */

    public UdpClientPacket(Peer peer, byte[] buffer)
    {
        _peer = peer;
        Buffer = buffer;
        Authenticated = false;
    }
    
    public async Task SendAsync<T>(T packet) where T : class
    {
        if (_peer.State != PeerState.Connected)
        {
            return;
        }
        
        await using var stream = new MemoryStream();
        Serializer.SerializeWithLengthPrefix(stream, packet, PrefixStyle.Base128);
        
        Packet p = new Packet();
        p.Create(stream.ToArray(), PacketFlags.Reliable);
        if (!_peer.Send(0, ref p))
        {
            Console.WriteLine("Failed to send udp packet to client");
        }
    }

    public void Dispose()
    {
    }
}
