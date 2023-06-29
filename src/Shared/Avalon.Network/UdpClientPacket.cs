using Avalon.Network.Packets.Abstractions;
using ENet;
using ProtoBuf;

namespace Avalon.Network;

public delegate void UdpBroadcastCallback(byte arg1, ref Packet packet);

public class UdpClientPacket : IRemoteSource
{
    public long RoundTripTime => _peer.RoundTripTime;
    public string RemoteAddress => $"{_peer.IP}:{_peer.Port}";

    public byte[] Buffer { get; }
    public bool Authenticated { get; }
    public PeerState State => _peer.State;
    //public string Id => _peer.ID.ToString();
    
    private Peer _peer;
    private readonly UdpBroadcastCallback _responseCallback;

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

    public UdpClientPacket(Peer peer, byte[] buffer, UdpBroadcastCallback responseCallback)
    {
        _peer = peer;
        _responseCallback = responseCallback;
        Buffer = buffer;
        Authenticated = false;
    }
    
    public async Task SendAsync(NetworkPacket packet)
    {
        if (_peer.State != PeerState.Connected)
        {
            Console.WriteLine($"Failed to send udp packet {packet.Header.Type} to client {_peer.IP}:{_peer.Port} because the client is not connected.");
            return;
        }

        await using var stream = new MemoryStream();
        Serializer.SerializeWithLengthPrefix(stream, packet, PrefixStyle.Base128);
        
        var responsePacket = new Packet();
        responsePacket.Create(stream.ToArray(), PacketFlags.Reliable);
        if (!_peer.Send(0, ref responsePacket))
        {
            Console.WriteLine($"Failed to send udp packet {packet.Header.Type} to client {_peer.IP}:{_peer.Port}");
        }
        responsePacket.Dispose();
    }

    public void Dispose()
    {
    }
}
