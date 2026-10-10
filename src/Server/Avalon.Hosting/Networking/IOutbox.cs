using Avalon.Network.Packets.Abstractions;

namespace Avalon.Hosting.Networking;

/// <remarks>
/// Lifecycle: <see cref="Connect"/> is called once after the stream is ready.
/// <see cref="Flush"/> is a no-op on <c>ChannelOutbox</c> (bg task drains); on
/// <c>TickDrivenOutbox</c> it seals, frames and writes the queued packets.
/// </remarks>
public interface IOutbox : IAsyncDisposable
{
    void Connect(PacketStream stream);

    /// <summary>Takes the packet's payload reference: queues the packet, or refuses it and releases it.</summary>
    bool Enqueue(OutboundPacket packet);

    void Flush();
}
