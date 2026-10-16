using Avalon.Common.GameAuth;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Generic;
using Avalon.World.GameAuth;
using Avalon.World.Public;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Maintenance;

/// <summary>
/// The close of a connection whose entry the world entry gate refused, at character select or at the final spawn. The
/// client is told maintenance, as it always was. Identity is told maintenance only for a cutoff in force, which releases
/// the place (and the world's held places with it); an account refused (no longer active) is told as a kick; any other
/// refusal (the check failed or was slow, no maintenance row, a decision that expired before it was applied) is the
/// world's fault, as a select it could not finish is, and holds the place as a drop (#967). Tick only, as its callers
/// are.
/// </summary>
public static class EntryRefusal
{
    public const string Message = "World is under maintenance";

    /// <param name="maintenance">
    /// The world's maintenance coordinator, when it has one: then a cutoff is the gate's verdict or the cutoff (or restart
    /// drain) the world applies now (<see cref="WorldMaintenanceCoordinator.CutsOff" />), never a decision's deadline alone,
    /// which an operator may have lifted or moved since. Without one, an allowed decision that ended at the deadline is the
    /// cutoff's.
    /// </param>
    public static void Close(IWorldConnection connection, WorldEntryDecision decision, ILogger logger,
        WorldMaintenanceCoordinator? maintenance = null)
    {
        bool cutoff = decision.Cutoff ||
            (maintenance is not null ? maintenance.CutsOff(connection) : decision.Allowed && decision.EndsAtCutoff);
        if (decision.AccountRefused) SessionEndReasons.Note(connection, GameSessionEndReasons.Kicked);
        else if (!cutoff) SessionEndReasons.Note(connection, GameSessionEndReasons.Drop);
#pragma warning disable MA0045 // a tick-thread caller; the close finishes on its own
        GracefulShutdownHelper.NotifyAndClose(connection, Message, DisconnectReason.Maintenance, logger);
#pragma warning restore MA0045
    }
}
