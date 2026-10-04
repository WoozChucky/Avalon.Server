using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.World;
using Avalon.World.Public;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Packet = Avalon.Network.Packets.Packet;

namespace Avalon.Server.World.UnitTests.GameAuth;

public sealed class WorldAdmissionPolicyShould
{
    [Theory]
    [InlineData(0x200F)]
    [InlineData(0x201B)]
    [InlineData(0x301B)]
    [InlineData(0x201C)]
    [InlineData(0x301C)]
    public void Retired_gameplay_admission_opcodes_are_unassigned(int opcode) =>
        Assert.False(Enum.IsDefined((NetworkPacketType)opcode));

    [Fact]
    public void Account_identity_alone_cannot_dispatch_gameplay_and_pending_admission_cannot_dispatch_it()
    {
        var server = Substitute.For<IWorldServer, IServerBase>();
        var handler = new Recorder();
        server.PacketHandlers.Returns(new Dictionary<NetworkPacketType, IWorldPacketHandler> { [NetworkPacketType.CMSG_CHARACTER_LIST] = handler });
        using var connection = WorldAdmissionConnection.Create(server);
        connection.AccountId = 42;
        connection.Deliver(NetworkPacketType.CMSG_CHARACTER_LIST, new CCharacterListPacket());
        connection.UpdateSession(); Assert.Equal(0, handler.Calls);
        Assert.True(connection.TryBeginAdmission());
        connection.Deliver(NetworkPacketType.CMSG_CHARACTER_LIST, new CCharacterListPacket());
        connection.UpdateSession(); Assert.Equal(0, handler.Calls);
    }
    [Fact]
    public void Recheck_authority_at_dispatch_and_drop_packets_queued_before_expiry()
    {
        var server = Substitute.For<IWorldServer, IServerBase>(); var handler = new Recorder();
        server.PacketHandlers.Returns(new Dictionary<NetworkPacketType, IWorldPacketHandler> { [NetworkPacketType.CMSG_CHARACTER_LIST] = handler });
        using var connection = WorldAdmissionConnection.Create(server);
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var lease = WorldAdmissionConnection.Lease(clock);
        connection.PublishAdmission(lease); Assert.True(connection.AcceptProtocol());
        connection.Deliver(NetworkPacketType.CMSG_CHARACTER_LIST, new CCharacterListPacket());
        clock.Advance(TimeSpan.FromSeconds(44));
        connection.UpdateSession(); Assert.Equal(0, handler.Calls);
    }
    [Fact]
    public void Never_publish_a_second_writer_or_enable_packets_before_protocol_acceptance()
    {
        using var connection = WorldAdmissionConnection.Create(); var lease = WorldAdmissionConnection.Lease();
        connection.PublishAdmission(lease);
        Assert.False(connection.IsGameplayAuthorized);
        Assert.Throws<InvalidOperationException>(() => connection.PublishAdmission(WorldAdmissionConnection.Lease()));
        Assert.Same(lease.Authority, connection.GameplayAuthority);
        Assert.Throws<InvalidOperationException>(() => connection.AccountId = 43);
        Assert.Throws<InvalidOperationException>(() => connection.AssignAccessLevel(Avalon.Common.Accounts.AccountAccessLevel.Admin));
        Assert.True(connection.AcceptProtocol()); Assert.True(connection.IsGameplayAuthorized);
        Assert.False(connection.AcceptProtocol());
        lease.Revoke(); Assert.False(connection.IsGameplayAuthorized);
    }
    private sealed class Recorder : IWorldPacketHandler
    {
        public int Calls { get; private set; }
        public void Execute(IWorldConnection connection, Packet packet) => Calls++;
    }
}
