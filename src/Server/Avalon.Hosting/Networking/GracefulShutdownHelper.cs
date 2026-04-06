using Avalon.Network.Packets;
using Avalon.Network.Packets.Generic;
using Microsoft.Extensions.Logging;

namespace Avalon.Hosting.Networking;

/// <summary>
/// Shared utility for sending a disconnect notification to a connection and then closing it.
/// Used by AuthServer, WorldServer (graceful stop) and WorldServer (forced kick).
/// </summary>
public static class GracefulShutdownHelper
{
    /// <summary>
    /// Sends a <see cref="SDisconnectPacket"/> to <paramref name="connection"/> and then closes it.
    /// A failed send is logged but never prevents the close from being called.
    /// </summary>
    public static void NotifyAndClose(
        IConnection connection,
        string reason,
        DisconnectReason reasonCode,
        ILogger? logger = null)
    {
        try
        {
            connection.Send(SDisconnectPacket.Create(reason, reasonCode));
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to send disconnect packet to {EndPoint}", connection.RemoteEndPoint);
        }

        connection.Close();
    }
}
