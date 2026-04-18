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

        probe.Account(NetworkPacketType.CMSG_MOVEMENT, 24);
        probe.Account(NetworkPacketType.CMSG_MOVEMENT, 24);
        probe.Account(NetworkPacketType.CMSG_PONG, 16);

        ConcurrentDictionary<NetworkPacketType, long> snapshot = probe.SnapshotPacketTypeCounts();

        Assert.Equal(2L, snapshot[NetworkPacketType.CMSG_MOVEMENT]);
        Assert.Equal(1L, snapshot[NetworkPacketType.CMSG_PONG]);
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
