using Avalon.Network.Packets.Abstractions;

namespace Avalon.Hosting.Networking;

/// <summary>
/// A connection's outbound side: the auth server's <c>ChannelOutbox</c>, drained by its own task, or the world server's
/// <c>ConnectionSender</c>, written by its send thread (#875). Nothing outside it writes to the stream.
/// </summary>
/// <remarks>
/// Lifecycle: <see cref="Connect"/> is called once after the stream is ready; <see cref="IAsyncDisposable.DisposeAsync"/>
/// once, by the connection's close, which waits within a budget for what is queued to go out.
/// </remarks>
public interface IOutbox : IAsyncDisposable
{
    void Connect(PacketStream stream);

    /// <summary>Takes the packet's payload reference: queues the packet, or refuses it and releases it.</summary>
    bool Enqueue(OutboundPacket packet);
}
