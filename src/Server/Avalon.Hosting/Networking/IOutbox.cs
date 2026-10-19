using Avalon.Network.Packets.Abstractions;

namespace Avalon.Hosting.Networking;

/// <summary>
/// A connection's outbound side: the world server's <c>ConnectionSender</c> is written by its send thread (#875).
/// Nothing outside the outbox writes to the stream.
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

    /// <summary>
    /// Whether this outbox seals the packets flagged Encrypted (it was given a sealer); the byte counters add the seal's
    /// overhead then.
    /// </summary>
    bool Seals { get; }
}
