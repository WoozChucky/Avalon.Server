using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Generic;
using Avalon.Server.World.UnitTests.GameAuth;
using Avalon.World;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.WorldConnection;

/// <summary>
/// With <c>Network:PacketEncryption</c> on (#875), every packet the world runs arrives sealed from a client that follows
/// its admission reply: a plain one is a protocol violation and closes the connection. Admission and pong are plain by
/// design, and an opcode the world never runs is dropped as before, whatever its flags. With the flag off both are
/// accepted, so today's client, which seals, still plays.
/// </summary>
public sealed class PacketEncryptionStrictnessShould
{
    public static TheoryData<bool, NetworkPacketType, NetworkPacketFlags, bool> Cases() => new()
    {
        // Sealed by this world: a plain packet it runs, the version handshake included, is refused; a sealed one is not.
        { true, NetworkPacketType.CMSG_CHARACTER_LIST, NetworkPacketFlags.None, true },
        { true, NetworkPacketType.CMSG_WORLD_HANDSHAKE, NetworkPacketFlags.None, true },
        { true, NetworkPacketType.CMSG_CHARACTER_LIST, NetworkPacketFlags.Encrypted, false },
        // Plain by design: admission and pong. A ClearText opcode the world never runs is dropped, never refused.
        { true, NetworkPacketType.CMSG_GAME_ADMISSION, NetworkPacketFlags.ClearText, false },
        { true, NetworkPacketType.CMSG_PONG, NetworkPacketFlags.None, false },
        { true, NetworkPacketType.CMSG_PING, NetworkPacketFlags.ClearText, false },
        // TLS alone: plain and sealed are both accepted.
        { false, NetworkPacketType.CMSG_CHARACTER_LIST, NetworkPacketFlags.None, false },
        { false, NetworkPacketType.CMSG_CHARACTER_LIST, NetworkPacketFlags.Encrypted, false },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Refuse_a_plain_gameplay_packet_only_while_packets_are_sealed(bool packetEncryption, NetworkPacketType type,
        NetworkPacketFlags flags, bool refused)
    {
        IWorldServer server = Substitute.For<IWorldServer, IServerBase>();
        server.PacketEncryption.Returns(packetEncryption);
        using var connection = WorldAdmissionConnection.Create(server);
        GameplayTestAdmission.Admit(connection);

        connection.Deliver(type, type switch
        {
            NetworkPacketType.CMSG_CHARACTER_LIST => new CCharacterListPacket(),
            NetworkPacketType.CMSG_WORLD_HANDSHAKE => new CWorldHandshakePacket { Version = "0.2.0" },
            NetworkPacketType.CMSG_GAME_ADMISSION => new CGameAdmissionPacket(),
            NetworkPacketType.CMSG_PONG => new CPongPacket(),
            _ => new CPingPacket(),
        }, flags);

        Assert.Equal(refused, connection.IsClosing);
    }
}
