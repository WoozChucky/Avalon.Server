using System.Reflection;
using Avalon.Network.Packets.Auth;
using Avalon.Server.World.Handlers;
using Avalon.Server.World.UnitTests.GameAuth;
using Avalon.World;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ProtoBuf;

namespace Avalon.Server.World.UnitTests.Handlers;

public sealed class WorldHandshakeHandlerShould
{
    private static WorldHandshakeHandler Handler(string minimum = "0.0.1")
    {
        IWorld world = Substitute.For<IWorld>(); world.MinVersion.Returns(minimum);
        return new(world, NullLogger<WorldHandshakeHandler>.Instance);
    }
    // A handshake can be read between the admission's publication of its lease and of the connection's AccountId
    // (PublishAdmission): leaseOnly leaves the connection in that gap.
    private static void Admit(WorldAdmissionConnection connection, bool leaseOnly)
    {
        if (!leaseOnly) { connection.PublishAdmission(WorldAdmissionConnection.Lease()); return; }
        typeof(Avalon.World.WorldConnection).GetField("_gameSessionLease", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(connection, WorldAdmissionConnection.Lease());
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Accept_supported_client_only_after_admission_and_request_the_initial_ping(bool leaseOnly)
    {
        using var connection = WorldAdmissionConnection.Create();
        Admit(connection, leaseOnly);
        await Handler().ExecuteAsync(new() { Connection = connection, Packet = new() { Version = "0.3.0" } });
        Assert.True(connection.IsGameplayAuthorized);
        // The acceptance is what it was before refusals carried a reason: account 42 and Verified, nothing else.
        Assert.Equal([0x08, 0x2A, 0x10, 0x01], Assert.Single(connection.Sent).PayloadMemory.ToArray());
        Assert.True(connection.TakeInitialTimeSyncPingRequest());
        Assert.False(connection.TakeInitialTimeSyncPingRequest());
    }
    [Theory]
    [InlineData("0.0.1", "0.1.0", "0.3.0", false)]
    [InlineData("0.0.1", "0.2.0", "0.3.0", true)]
    [InlineData("0.0.1", "invalid", "0.3.0", false)]
    [InlineData("0.0.1", "", "0.3.0", false)]
    [InlineData("0.4.0", "0.3.0", "0.4.0", false)]
    [InlineData("1.0.0", "0.2.0", "1.0.0", false)]
    [InlineData(" 01.00.0", "0.9.9", "1.0.0", false)]
    public async Task Tell_an_old_or_malformed_client_the_version_it_needs_then_close(string minimum, string version,
        string required, bool leaseOnly)
    {
        using var connection = WorldAdmissionConnection.Create();
        Admit(connection, leaseOnly);
        await Handler(minimum).ExecuteAsync(new() { Connection = connection, Packet = new() { Version = version } });
        SWorldHandshakePacket reply = Serializer.Deserialize<SWorldHandshakePacket>(Assert.Single(connection.Sent).PayloadMemory);
        Assert.False(reply.Verified);
        Assert.Equal(WorldHandshakeRefusal.ClientVersionTooOld, reply.Refusal);
        Assert.Equal(required, reply.MinVersion);
        Assert.Equal(42, reply.AccountId);
        Assert.True(connection.IsClosing); Assert.False(connection.IsGameplayAuthorized);
        Assert.False(connection.TakeInitialTimeSyncPingRequest());
    }
    [Fact]
    public async Task Reject_account_id_without_admitted_authority()
    {
        using var connection = WorldAdmissionConnection.Create(); connection.AccountId = 42;
        await Handler().ExecuteAsync(new() { Connection = connection, Packet = new() { Version = "0.1.0" } });
        Assert.True(connection.IsClosing); Assert.False(connection.IsGameplayAuthorized);
        Assert.Empty(connection.Sent);
    }
    [Fact]
    public async Task Close_a_second_handshake_without_a_reply_whatever_its_version()
    {
        using var connection = WorldAdmissionConnection.Create(); connection.PublishAdmission(WorldAdmissionConnection.Lease());
        await Handler().ExecuteAsync(new() { Connection = connection, Packet = new() { Version = "0.3.0" } });
        await Handler().ExecuteAsync(new() { Connection = connection, Packet = new() { Version = "0.1.0" } });
        Assert.True(connection.IsClosing);
        Assert.Single(connection.Sent);
    }
}
