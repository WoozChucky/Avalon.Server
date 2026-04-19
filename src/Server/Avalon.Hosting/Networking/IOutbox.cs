using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;

namespace Avalon.Hosting.Networking;

/// <remarks>
/// Lifecycle: <see cref="Connect"/> is called once after the stream is ready.
/// <see cref="Flush"/> is a no-op on <c>ChannelOutbox</c> (bg task drains); on
/// <c>TickDrivenOutbox</c> it serializes queued packets and kicks off a write.
/// </remarks>
public interface IOutbox : IAsyncDisposable
{
    void Connect(PacketStream stream);
    void Enqueue(NetworkPacket packet);
    void Flush();
}
