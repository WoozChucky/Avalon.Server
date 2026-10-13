using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Generic;
using Avalon.Network.Packets.Movement;
using Avalon.World.Public.Characters;
using Avalon.World.Testing;
using Packet = Avalon.Network.Packets.Packet;

namespace Avalon.Server.World.UnitTests.Performance;

public class ReceiveQueueAllocationShould
{
    [Theory]
    [InlineData(1)]
    [InlineData(32)]
    [InlineData(150)]
    public void Reuse_receive_queue_storage_after_warmup(int packetsPerTick)
    {
        using var fixture = new ReceiveQueueFixture();
        var input = new CPlayerInputPacket();
        Pump(fixture, NetworkPacketType.CMSG_PLAYER_INPUT, input, packetsPerTick, 1_000);
        int dispatchedBefore = fixture.Dispatched;
        long before = ThreadAllocations.BeforeQuietWindow();
        Pump(fixture, NetworkPacketType.CMSG_PLAYER_INPUT, input, packetsPerTick, 1_000);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(packetsPerTick * 1_000, fixture.Dispatched - dispatchedBefore);
        Assert.Equal(0, allocated);
    }

    /// <summary>
    /// A pong beyond one per ping is dropped as it arrives (#892), and a client can send them at any rate: after the
    /// ping's one answer, the drop allocates nothing and nothing reaches the queue or a handler. The decode of each
    /// frame, upstream of the arrival, is not in this.
    /// </summary>
    [Fact]
    public void Drop_a_pong_beyond_one_per_ping_without_allocating()
    {
        using var fixture = new ReceiveQueueFixture(Stream.Null);
        var pong = new CPongPacket { LastServerTimestamp = fixture.Clock.Now.UtcTicks };
        fixture.Connection.SendTimeSyncPing();
        fixture.Scheduler!.RunAllPasses();
        fixture.Connection.Deliver(NetworkPacketType.CMSG_PONG, pong);
        fixture.Pump();
        Assert.Equal(1, fixture.Dispatched);

        Pump(fixture, NetworkPacketType.CMSG_PONG, pong, 32, 1_000);
        long before = ThreadAllocations.BeforeQuietWindow();
        Pump(fixture, NetworkPacketType.CMSG_PONG, pong, 32, 1_000);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(1, fixture.Dispatched);
        Assert.Equal(0, allocated);
    }

    /// <summary>
    /// In-map packets still queued when their character has gone are dropped by the session pass (#663), and a client
    /// causes one with every packet it sends between its leave and the answer to it. The drop allocates nothing: its
    /// log line cost an array and a boxed opcode for every packet, with Debug off (#883). Input and any other packet
    /// take different lines.
    /// </summary>
    [Fact]
    public void Drop_packets_queued_for_a_character_that_has_gone_without_allocating()
    {
        using var fixture = new ReceiveQueueFixture();
        ICharacter character = fixture.Connection.Character!;
        var input = new CPlayerInputPacket();
        var cast = new CCastAbilityPacket();

        int leftQueued = LeaveBehind(1_000);
        long before = ThreadAllocations.BeforeQuietWindow();
        leftQueued += LeaveBehind(1_000);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, leftQueued);
        Assert.Equal(0, fixture.Dispatched);
        Assert.Equal(0, allocated);

        int LeaveBehind(int rounds)
        {
            int left = 0;
            for (int round = 0; round < rounds; round++)
            {
                fixture.Connection.Character = character;
                for (int packet = 0; packet < 16; packet++)
                {
                    fixture.Connection.Deliver(NetworkPacketType.CMSG_PLAYER_INPUT, input);
                    fixture.Connection.Deliver(NetworkPacketType.CMSG_CAST_ABILITY, cast);
                }

                fixture.Connection.Character = null;
                fixture.Connection.UpdateSession();
                left += fixture.QueueDepth;
            }

            return left;
        }
    }

    private static void Pump(ReceiveQueueFixture fixture, NetworkPacketType type, Packet packet, int packetsPerTick, int ticks)
    {
        for (int tick = 0; tick < ticks; tick++)
        {
            for (int delivered = 0; delivered < packetsPerTick; delivered++)
                fixture.Connection.Deliver(type, packet);
            fixture.Pump();
        }
    }
}
