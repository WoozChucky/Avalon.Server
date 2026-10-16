using Avalon.Common.GameAuth;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Generic;
using Avalon.Server.World.UnitTests.GameAuth;
using Avalon.World.Entities;
using Avalon.World.Filters;
using Avalon.World.Handlers;

namespace Avalon.Server.World.UnitTests.Handlers;

/// <summary>
/// #967: the goodbye ends the session as a logout, which releases the place at once, before a character or in a map,
/// whatever closes the connection after it.
/// </summary>
public sealed class LeaveWorldShould
{
    [Theory]
    [InlineData(false, "peer")]
    [InlineData(true, "peer")]
    [InlineData(true, "world")] // the world closes it for a reason that would hold the place, after the goodbye
    public void End_the_session_as_a_logout_whatever_closes_the_connection_after_the_goodbye(bool inMap, string closedBy)
    {
        using WorldAdmissionConnection connection = Admitted(inMap);
        Assert.True(new WorldSessionFilter(connection).CanProcess(NetworkPacketType.CMSG_LEAVE_WORLD));

        connection.Deliver(NetworkPacketType.CMSG_LEAVE_WORLD, new CLeaveWorldPacket());
        // No tick ran the handler: the close comes first, as when the client closes right after its goodbye.
        if (closedBy == "peer") connection.Close(false);
        else GracefulShutdownHelper.NotifyAndClose(connection, "slow", DisconnectReason.SlowConnection);

        Assert.Equal(GameSessionEndReasons.Logout, connection.SessionEndReason);
    }

    [Fact]
    public void Close_a_connection_whose_client_said_goodbye_and_stayed()
    {
        using WorldAdmissionConnection connection = Admitted(inMap: false);

        new LeaveWorldHandler().Execute(connection, new CLeaveWorldPacket());

        Assert.True(connection.IsClosing);
        Assert.Equal(GameSessionEndReasons.Logout, connection.SessionEndReason);
    }

    private static WorldAdmissionConnection Admitted(bool inMap)
    {
        var connection = WorldAdmissionConnection.Create();
        GameplayTestAdmission.Admit(connection, new AccountId(42));
        if (inMap) connection.Character = new CharacterEntity { Data = new Character { Id = new CharacterId(7), Name = "Tester", Map = 1 } };
        return connection;
    }
}
