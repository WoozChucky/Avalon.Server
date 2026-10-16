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
/// whatever closes the connection after it; a goodbye before the version handshake, or after the world closed the
/// connection for a reason of its own, changes nothing.
/// </summary>
public sealed class LeaveWorldShould
{
    [Theory]
    [InlineData(false, "goodbye, peer close", GameSessionEndReasons.Logout)]
    [InlineData(true, "goodbye, peer close", GameSessionEndReasons.Logout)]
    // The world closes it for a reason that would hold the place, after the goodbye.
    [InlineData(true, "goodbye, world close", GameSessionEndReasons.Logout)]
    // Admitted, its version handshake not accepted: the goodbye is dropped as any gameplay packet is then.
    [InlineData(false, "no handshake, goodbye, peer close", GameSessionEndReasons.Drop)]
    // The world closed it for a ban first: the first reason noted stands.
    [InlineData(true, "banned close, goodbye", GameSessionEndReasons.Kicked)]
    public void Report_the_goodbye_only_when_it_is_the_first_reason_on_an_accepted_connection(bool inMap, string sequence,
        string expected)
    {
        bool handshake = !sequence.StartsWith("no handshake", StringComparison.Ordinal);
        using WorldAdmissionConnection connection = Admitted(inMap, handshake);
        // The session filter takes the goodbye once the connection's version handshake was accepted, in every state.
        Assert.Equal(handshake, new WorldSessionFilter(connection).CanProcess(NetworkPacketType.CMSG_LEAVE_WORLD));
        if (sequence.StartsWith("banned", StringComparison.Ordinal))
        {
            GracefulShutdownHelper.NotifyAndClose(connection, "banned", DisconnectReason.Banned);
        }

        connection.Deliver(NetworkPacketType.CMSG_LEAVE_WORLD, new CLeaveWorldPacket());
        // No tick ran the handler: the close comes first, as when the client closes right after its goodbye.
        if (sequence.EndsWith("peer close", StringComparison.Ordinal))
        {
            connection.Close(false);
        }
        else if (sequence.EndsWith("world close", StringComparison.Ordinal))
        {
            GracefulShutdownHelper.NotifyAndClose(connection, "slow", DisconnectReason.SlowConnection);
        }

        Assert.Equal(expected, connection.SessionEndReason);
    }

    [Fact]
    public void Close_a_connection_whose_client_said_goodbye_and_stayed()
    {
        using WorldAdmissionConnection connection = Admitted(inMap: false);

        new LeaveWorldHandler().Execute(connection, new CLeaveWorldPacket());

        Assert.True(connection.IsClosing);
        Assert.Equal(GameSessionEndReasons.Logout, connection.SessionEndReason);
    }

    private static WorldAdmissionConnection Admitted(bool inMap, bool handshake = true)
    {
        var connection = WorldAdmissionConnection.Create();
        if (handshake) GameplayTestAdmission.Admit(connection, new AccountId(42));
        else connection.PublishAdmission(WorldAdmissionConnection.Lease());
        if (inMap) connection.Character = new CharacterEntity { Data = new Character { Id = new CharacterId(7), Name = "Tester", Map = 1 } };
        return connection;
    }
}
