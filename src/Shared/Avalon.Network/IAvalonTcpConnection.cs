using Avalon.Network.Packets.Abstractions;

namespace Avalon.Network;

/// <summary>
/// Represents an accepted server-side TCP connection that can be tracked, sent packets, and closed.
/// </summary>
public interface IAvalonTcpConnection : IDisposable
{
    /// <summary>Unique identifier for this connection instance.</summary>
    Guid Id { get; }

    /// <summary>
    /// Fired when this connection is closed or disposed.
    /// The argument is the connection's <see cref="Id"/>.
    /// </summary>
    event Action<Guid>? Disconnected;

    /// <summary>Closes the connection and releases all associated resources.</summary>
    void Close();

    /// <summary>Sends a packet to the remote endpoint.</summary>
    Task SendAsync(NetworkPacket packet);
}
