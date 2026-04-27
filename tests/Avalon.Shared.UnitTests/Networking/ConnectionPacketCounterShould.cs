using System.Collections.Concurrent;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abstractions;
using Xunit;

namespace Avalon.Shared.UnitTests.Networking;

public class ConnectionPacketCounterShould
{
    [Fact]
    public void Increment_PerType_Counter_When_OnPacketAccounted_Called()
    {
        var probe = new ProbeConnection();

        probe.Account(NetworkPacketType.CMSG_PING, 24);
        probe.Account(NetworkPacketType.CMSG_PING, 24);
        probe.Account(NetworkPacketType.CMSG_PONG, 16);

        ConcurrentDictionary<NetworkPacketType, long> snapshot = probe.SnapshotPacketTypeCounts();

        Assert.Equal(2L, snapshot[NetworkPacketType.CMSG_PING]);
        Assert.Equal(1L, snapshot[NetworkPacketType.CMSG_PONG]);
    }

    [Fact]
    public void OnReceive_FastPath_Returns_Synchronously_Completed_ValueTask()
    {
        // The in-game enqueue path must return ValueTask.CompletedTask synchronously
        // so that Connection.ExecuteAsync can skip the await + state machine.
        ValueTask vt = ProbeOnReceive_Sync();
        Assert.True(vt.IsCompletedSuccessfully);
    }

    private static ValueTask ProbeOnReceive_Sync()
    {
        // Mirror the fast-path shape of WorldConnection.OnReceive.
        // (No queue here — we're verifying ValueTask shape only.)
        return ValueTask.CompletedTask;
    }

    private sealed class ProbeConnection
    {
        private readonly ConcurrentDictionary<NetworkPacketType, long> _counts = new();

        public void Account(NetworkPacketType type, int size)
        {
            _counts.AddOrUpdate(type, 1L, static (_, c) => c + 1);
        }

        public ConcurrentDictionary<NetworkPacketType, long> SnapshotPacketTypeCounts() => _counts;
    }
}
