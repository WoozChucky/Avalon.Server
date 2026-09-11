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
        Notify(connection, reason, reasonCode, logger);
        connection.Close();
    }

    /// <summary>
    /// As <see cref="NotifyAndClose"/>, but returns only once the connection has finished closing.
    /// For callers that are about to stop the process: the notification is delivered from the
    /// close, so a host that exits without waiting for it sends nothing.
    /// </summary>
    public static Task NotifyAndCloseAsync(
        IConnection connection,
        string reason,
        DisconnectReason reasonCode,
        ILogger? logger = null)
    {
        Notify(connection, reason, reasonCode, logger);
        return connection.CloseAsync();
    }

    private static void Notify(IConnection connection, string reason, DisconnectReason reasonCode, ILogger? logger)
    {
        try
        {
            connection.Send(SDisconnectPacket.Create(reason, reasonCode));
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to send disconnect packet to {EndPoint}", connection.RemoteEndPoint);
        }
    }
}
