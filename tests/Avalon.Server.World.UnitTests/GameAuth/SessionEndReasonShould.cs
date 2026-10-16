using Avalon.Common.GameAuth;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Generic;

namespace Avalon.Server.World.UnitTests.GameAuth;

/// <summary>#967: the reason a world reports for a session's end decides whether its place is held.</summary>
public sealed class SessionEndReasonShould
{
    [Theory]
    [InlineData(null, GameSessionEndReasons.Drop)]
    [InlineData(DisconnectReason.ServerShutdown, GameSessionEndReasons.Shutdown)]
    [InlineData(DisconnectReason.Maintenance, GameSessionEndReasons.Maintenance)]
    [InlineData(DisconnectReason.Kicked, GameSessionEndReasons.Kicked)]
    [InlineData(DisconnectReason.Banned, GameSessionEndReasons.Kicked)]
    [InlineData(DisconnectReason.Deactivated, GameSessionEndReasons.Kicked)]
    [InlineData(DisconnectReason.SelectTimeout, GameSessionEndReasons.Drop)] // the world was busy or the select stalled
    [InlineData(DisconnectReason.CharacterSaveFailed, GameSessionEndReasons.Drop)]
    [InlineData(DisconnectReason.DuplicateLogin, GameSessionEndReasons.Drop)]
    public void Report_why_the_world_closed_the_connection(DisconnectReason? closedWith, string expected)
    {
        using var connection = WorldAdmissionConnection.Create();
        if (closedWith is { } reason) GracefulShutdownHelper.NotifyAndClose(connection, "closing", reason);
        else connection.Close(false);

        Assert.Equal(expected, connection.SessionEndReason);
    }
}
