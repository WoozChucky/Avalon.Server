using Avalon.Common.GameAuth;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Generic;
using Avalon.World.GameAuth;
using Avalon.World.Public;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Maintenance;

/// <summary>
/// The close of a connection whose entry the world entry gate refused, at character select or at the final spawn. The
/// client is told maintenance, as it always was. Identity is told maintenance only for a verdict (a cutoff, an account
/// not allowed), which releases the place; a check that failed is the world's fault, as a select it could not finish
/// is, and holds the place as a drop (#967). Tick only, as its callers are.
/// </summary>
public static class EntryRefusal
{
    public const string Message = "World is under maintenance";

    public static void Close(IWorldConnection connection, WorldEntryDecision decision, ILogger logger)
    {
        if (decision.Failed) SessionEndReasons.Note(connection, GameSessionEndReasons.Drop);
#pragma warning disable MA0045 // a tick-thread caller; the close finishes on its own
        GracefulShutdownHelper.NotifyAndClose(connection, Message, DisconnectReason.Maintenance, logger);
#pragma warning restore MA0045
    }
}
