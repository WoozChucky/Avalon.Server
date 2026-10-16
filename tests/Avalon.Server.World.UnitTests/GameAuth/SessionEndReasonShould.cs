using Avalon.Common.GameAuth;
using Avalon.Common.ValueObjects;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Generic;
using Avalon.World.Maintenance;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Server.World.UnitTests.GameAuth;

/// <summary>#967: the reason a world reports for a session's end decides whether its place is held.</summary>
public sealed class SessionEndReasonShould
{
    [Theory]
    [InlineData("peer", null, GameSessionEndReasons.Drop)]
    [InlineData("notice", DisconnectReason.ServerShutdown, GameSessionEndReasons.Shutdown)]
    [InlineData("notice", DisconnectReason.Maintenance, GameSessionEndReasons.Maintenance)]
    [InlineData("notice", DisconnectReason.Kicked, GameSessionEndReasons.Kicked)]
    [InlineData("notice", DisconnectReason.Banned, GameSessionEndReasons.Kicked)]
    [InlineData("notice", DisconnectReason.Deactivated, GameSessionEndReasons.Kicked)]
    [InlineData("notice", DisconnectReason.SelectTimeout, GameSessionEndReasons.Drop)] // the world was busy or the select stalled
    [InlineData("notice", DisconnectReason.CharacterSaveFailed, GameSessionEndReasons.Drop)]
    [InlineData("notice", DisconnectReason.DuplicateLogin, GameSessionEndReasons.Drop)]
    // A password, email or role change, an MFA reset, a refresh-token reuse: told Kicked, held (owner decision).
    [InlineData("account sessions ended", null, GameSessionEndReasons.Drop)]
    [InlineData("account banned", null, GameSessionEndReasons.Kicked)]
    [InlineData("entry refused", null, GameSessionEndReasons.Maintenance)]
    [InlineData("entry check failed", null, GameSessionEndReasons.Drop)] // told maintenance, but the world's fault
    [InlineData("entry refused for the account", null, GameSessionEndReasons.Kicked)] // no longer active: never a cutoff
    public void Report_why_the_world_closed_the_connection(string closedBy, DisconnectReason? reason, string expected)
    {
        using var connection = WorldAdmissionConnection.Create();
        GameplayTestAdmission.Admit(connection, new AccountId(42));
        switch (closedBy)
        {
            case "peer": connection.Close(false); break;
            case "notice": GracefulShutdownHelper.NotifyAndClose(connection, "closing", reason!.Value); break;
            case "account sessions ended": Avalon.World.WorldServer.CloseAccountSessions([connection], "42", NullLogger.Instance); break;
            case "account banned": Avalon.World.WorldServer.CloseBannedOrDeactivated([connection], "42|BANNED", NullLogger.Instance); break;
            case "entry refused": EntryRefusal.Close(connection, default, NullLogger.Instance); break;
            case "entry refused for the account":
                EntryRefusal.Close(connection, new WorldEntryDecision { AccountRefused = true }, NullLogger.Instance);
                break;
            default: EntryRefusal.Close(connection, new WorldEntryDecision { Failed = true }, NullLogger.Instance); break;
        }

        Assert.Equal(expected, connection.SessionEndReason);
    }
}
