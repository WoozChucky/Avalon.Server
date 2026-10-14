using Avalon.Configuration;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Generic;
using Avalon.Network.Packets.Movement;
using Avalon.Server.World.UnitTests.GameAuth;
using Avalon.World;
using Avalon.World.Public;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Packet = Avalon.Network.Packets.Packet;

namespace Avalon.Server.World.UnitTests.WorldConnection;

/// <summary>
/// With <c>Network:PacketEncryption</c> on (#875), every packet the world runs arrives sealed from a client that follows
/// its admission reply: a plain one is a protocol violation and closes the connection. Admission and pong are plain by
/// design, and an opcode the world never runs is dropped as before, whatever its flags. With the flag off a plain packet is
/// accepted; a sealed one never reaches this check, since a world that does not seal has no session and its read loop
/// closes the connection on it (#878, <c>PlainWorldConnectionShould</c>).
/// </summary>
public sealed class PacketEncryptionStrictnessShould
{
    public static TheoryData<bool, NetworkPacketType, NetworkPacketFlags, bool, bool> Cases() => new()
    {
        // Sealed by this world: a plain packet it runs, the version handshake and an in-map packet included, is refused;
        // a sealed one is queued for the tick.
        { true, NetworkPacketType.CMSG_CHARACTER_LIST, NetworkPacketFlags.None, true, false },
        { true, NetworkPacketType.CMSG_WORLD_HANDSHAKE, NetworkPacketFlags.None, true, false },
        { true, NetworkPacketType.CMSG_PLAYER_INPUT, NetworkPacketFlags.None, true, false },
        { true, NetworkPacketType.CMSG_CHARACTER_LIST, NetworkPacketFlags.Encrypted, false, true },
        // Plain by design: admission (run on arrival, never queued) and pong (the answer to the ping the connection
        // wrote, #892). A ClearText opcode the world never runs is dropped, never refused.
        { true, NetworkPacketType.CMSG_GAME_ADMISSION, NetworkPacketFlags.ClearText, false, false },
        { true, NetworkPacketType.CMSG_PONG, NetworkPacketFlags.None, false, true },
        { true, NetworkPacketType.CMSG_PING, NetworkPacketFlags.ClearText, false, false },
        // TLS alone: plain is accepted.
        { false, NetworkPacketType.CMSG_CHARACTER_LIST, NetworkPacketFlags.None, false, true },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Refuse_a_plain_gameplay_packet_only_while_packets_are_sealed(bool packetEncryption, NetworkPacketType type,
        NetworkPacketFlags flags, bool refused, bool queued)
    {
        List<NetworkPacketType> dispatched = [];
        IWorldServer server = Substitute.For<IWorldServer, IServerBase>();
        server.PacketEncryption.Returns(packetEncryption);
        server.PacketHandlers.Returns(new Dictionary<NetworkPacketType, IWorldPacketHandler>
        {
            [NetworkPacketType.CMSG_CHARACTER_LIST] = new Recorder(NetworkPacketType.CMSG_CHARACTER_LIST, dispatched),
            [NetworkPacketType.CMSG_PONG] = new Recorder(NetworkPacketType.CMSG_PONG, dispatched),
        });
        // Its send threads never start: the pass below writes the ping a pong answers.
        using var scheduler = new NetworkSendScheduler(new NetworkConfiguration { SendThreads = 1 }, NullLoggerFactory.Instance,
            TimeProvider.System, NetworkSendMetrics.Disabled);
        server.SendScheduler.Returns(scheduler);
        using var connection = WorldAdmissionConnection.Create(server);
        GameplayTestAdmission.Admit(connection);
        connection.InitOutboxForTest(new PacketStream(Stream.Null));
        connection.SendTimeSyncPing();
        scheduler.RunAllPasses();

        connection.Deliver(type, type switch
        {
            NetworkPacketType.CMSG_CHARACTER_LIST => new CCharacterListPacket(),
            NetworkPacketType.CMSG_WORLD_HANDSHAKE => new CWorldHandshakePacket { Version = "0.3.0" },
            NetworkPacketType.CMSG_PLAYER_INPUT => new CPlayerInputPacket(),
            NetworkPacketType.CMSG_GAME_ADMISSION => new CGameAdmissionPacket(),
            NetworkPacketType.CMSG_PONG => new CPongPacket(),
            _ => new CPingPacket(),
        }, flags);
        Assert.Equal(refused, connection.IsClosing);

        // The session pass runs what was queued at arrival.
        connection.UpdateSession();
        Assert.Equal(queued ? [type] : [], dispatched);

        connection.Close();
        scheduler.RunAllPasses();   // the close's last visit, so the sender finishes and gives back its buffers
    }

    private sealed class Recorder(NetworkPacketType type, List<NetworkPacketType> log) : IWorldPacketHandler
    {
        public void Execute(IWorldConnection connection, Packet packet) => log.Add(type);
    }
}
