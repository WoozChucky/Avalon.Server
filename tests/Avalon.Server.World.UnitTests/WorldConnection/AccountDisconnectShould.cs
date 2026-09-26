using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Generic;
using Avalon.World;
using Avalon.World.Public;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ProtoBuf;
using Xunit;

namespace Avalon.Server.World.UnitTests.WorldConnection;

/// <summary>
/// #504 review: a message on <c>world:accounts:disconnect</c> closed only the account's first
/// connection, so a second one (a duplicate session, or one whose close had not landed yet) kept
/// the old access after a role change. It also told every kicked player they had logged in from
/// another location, whatever the cause. The message is the bare account id and cannot say why,
/// so every connection of the account is closed, with a neutral reason.
/// </summary>
public sealed class AccountDisconnectShould
{
    private static IWorldConnection Connection(long accountId)
    {
        IWorldConnection connection = Substitute.For<IWorldConnection>();
        connection.AccountId.Returns(new AccountId(accountId));
        return connection;
    }

    private static SDisconnectPacket Sent(IWorldConnection connection)
    {
        NetworkPacket packet = connection.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IWorldConnection.Send))
            .Select(c => c.GetArguments()[0])
            .OfType<NetworkPacket>()
            .Single();
        using var stream = new MemoryStream(packet.Payload);
        return Serializer.Deserialize<SDisconnectPacket>(stream);
    }

    [Fact]
    public void Close_every_connection_of_the_account()
    {
        IWorldConnection first = Connection(7);
        IWorldConnection second = Connection(7);
        IWorldConnection other = Connection(8);

        int closed = WorldServer.CloseAccountSessions([first, other, second], "7", NullLogger.Instance);

        Assert.Equal(2, closed);
        first.Received(1).Close();
        second.Received(1).Close();
        other.DidNotReceiveWithAnyArgs().Close();
    }

    [Fact]
    public void Tell_each_one_its_session_ended_without_claiming_a_duplicate_login()
    {
        IWorldConnection connection = Connection(7);

        WorldServer.CloseAccountSessions([connection], "7", NullLogger.Instance);

        SDisconnectPacket sent = Sent(connection);
        Assert.Equal(DisconnectReason.Kicked, sent.ReasonCode);
        Assert.Equal("Your session has ended. Please log in again.", sent.Reason);
    }

    [Fact]
    public void Close_the_others_when_one_throws_while_closing()
    {
        IWorldConnection throwing = Connection(7);
        throwing.When(c => c.Close()).Do(_ => throw new InvalidOperationException("socket gone"));
        IWorldConnection next = Connection(7);

        int closed = WorldServer.CloseAccountSessions([throwing, next], "7", NullLogger.Instance);

        Assert.Equal(1, closed);
        next.Received(1).Close();
    }

    [Fact]
    public void Ignore_a_message_that_names_no_account()
    {
        IWorldConnection connection = Connection(7);

        int closed = WorldServer.CloseAccountSessions([connection], "not-an-id", NullLogger.Instance);

        Assert.Equal(0, closed);
        connection.DidNotReceiveWithAnyArgs().Close();
    }
}
