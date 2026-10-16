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

    /// <param name="cutoffApplied">The world's own applied cutoff, or its restart drain's, turned the connection away.</param>
    public static void Close(IWorldConnection connection, WorldEntryDecision decision, ILogger logger, bool cutoffApplied = false)
    {
        if (decision.AccountRefused) SessionEndReasons.Note(connection, GameSessionEndReasons.Kicked);
        else if (!(cutoffApplied || decision.Cutoff || (decision.Allowed && decision.EndsAtCutoff)))
            SessionEndReasons.Note(connection, GameSessionEndReasons.Drop);
#pragma warning disable MA0045 // a tick-thread caller; the close finishes on its own
        GracefulShutdownHelper.NotifyAndClose(connection, Message, DisconnectReason.Maintenance, logger);
#pragma warning restore MA0045
    }
}
