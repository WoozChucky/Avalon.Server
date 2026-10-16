using Avalon.Common.GameAuth;
using Avalon.Common.ValueObjects;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Generic;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.World.Configuration;
using Avalon.World.Maintenance;
using Avalon.World.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

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
    // An entry refusal is told maintenance; identity hears maintenance only for a cutoff in force.
    [InlineData("entry refused at a cutoff", null, GameSessionEndReasons.Maintenance)]
    [InlineData("entry allowed until a cutoff that has passed", null, GameSessionEndReasons.Maintenance)]
    [InlineData("entry refused, not for a cutoff", null, GameSessionEndReasons.Drop)] // a failed or slow check, no row, expired
    // Where the world applies maintenance, its applied cutoff decides: a deadline lifted since is no cutoff, and a check
    // that failed during the applied cutoff (or restart drain) is the cutoff's.
    [InlineData("entry allowed until a cutoff the world no longer applies", null, GameSessionEndReasons.Drop)]
    [InlineData("entry check failed during the world's applied cutoff", null, GameSessionEndReasons.Maintenance)]
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
            case "entry refused at a cutoff":
                EntryRefusal.Close(connection, new WorldEntryDecision { Cutoff = true }, NullLogger.Instance);
                break;
            case "entry allowed until a cutoff that has passed":
                EntryRefusal.Close(connection, new WorldEntryDecision(true, DateTime.UnixEpoch) { EndsAtCutoff = true }, NullLogger.Instance);
                break;
            case "entry refused for the account":
                EntryRefusal.Close(connection, new WorldEntryDecision { AccountRefused = true }, NullLogger.Instance);
                break;
            case "entry allowed until a cutoff the world no longer applies":
                EntryRefusal.Close(connection, new WorldEntryDecision(true, DateTime.UnixEpoch) { EndsAtCutoff = true },
                    NullLogger.Instance, Maintenance(new WorldMaintenanceState(false, 2, null)));
                break;
            case "entry check failed during the world's applied cutoff":
                EntryRefusal.Close(connection, default, NullLogger.Instance,
                    Maintenance(new WorldMaintenanceState(true, 1, DateTime.UtcNow.AddMinutes(-1))));
                break;
            default: EntryRefusal.Close(connection, default, NullLogger.Instance); break;
        }

        Assert.Equal(expected, connection.SessionEndReason);
    }

    /// <summary>A world's maintenance coordinator that has applied <paramref name="state" />.</summary>
    private static WorldMaintenanceCoordinator Maintenance(WorldMaintenanceState state)
    {
        var coordinator = new WorldMaintenanceCoordinator(new WorldId(1), Substitute.For<IWorldMaintenanceRepository>(),
            Substitute.For<ICharacterSaver>(), TimeProvider.System, NullLogger<WorldMaintenanceCoordinator>.Instance,
            Options.Create(new WorldShutdownConfiguration { SaveMargin = TimeSpan.FromMinutes(1) }));
        coordinator.ApplyCommitted(state);
        return coordinator;
    }
}
