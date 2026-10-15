using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Generic;
using Avalon.World.Public;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Characters;

/// <summary>
/// The answer to a character select, or its final spawn, that a full work queue refused (#1050, owner decision): the
/// connection is closed at once with <see cref="DisconnectReason.SelectTimeout" />, the answer the stalled-select sweep
/// gives a select that cannot finish, and the client logs in and selects again. The queue counted the refusal
/// (<c>world.work.refusals</c>); one Warning, without an exception, at most once per
/// <see cref="FailureLogThrottle.Interval" /> for the world. Tick only, as its callers are.
/// </summary>
internal static class BusyEntry
{
    private static readonly FailureLogThrottle s_refusals = new();

    public static void Close(IWorldConnection connection, ILogger logger, TimeProvider clock)
    {
        if (s_refusals.TryTake(clock, out int suppressed))
        {
            logger.LogWarning(
                "Closed a character select for account {AccountId}: a full work queue refused it; {Suppressed} more were refused since the last line",
                connection.AccountId, suppressed);
        }

#pragma warning disable MA0045 // a tick-thread caller; the close finishes on its own
        GracefulShutdownHelper.NotifyAndClose(connection, "The world is busy; try again", DisconnectReason.SelectTimeout,
            logger);
#pragma warning restore MA0045
    }
}
