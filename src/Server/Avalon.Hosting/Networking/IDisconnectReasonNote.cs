using Avalon.Network.Packets.Generic;

namespace Avalon.Hosting.Networking;

/// <summary>A connection that keeps why the server closed it (#967): the world reports it when the game session ends.</summary>
public interface IDisconnectReasonNote
{
    /// <summary>The first reason noted wins; called by <see cref="GracefulShutdownHelper"/> before its notice is sent.</summary>
    void NoteDisconnectReason(DisconnectReason reason);
}
