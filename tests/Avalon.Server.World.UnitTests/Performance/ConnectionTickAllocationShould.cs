using Avalon.Network.Packets.Abstractions;
using Avalon.Server.World.UnitTests.GameAuth;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World;
using Avalon.World.Characters;
using Avalon.World.Entities;
using Avalon.World.Filters;
using Avalon.World.Public;
using Avalon.World.Public.Instances;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Performance;

public class ConnectionTickAllocationShould
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Sweep_connections_without_allocating_while_no_spawn_is_due(bool waitingForLoad)
    {
        using var connection = WorldAdmissionConnection.Create();
        IWorldConnection[] connections = [connection];
        IWorld world = Substitute.For<IWorld>();
        long now = new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc).Ticks;
        if (waitingForLoad)
            connection.SetPendingSpawn(TestCharacters.New(), Substitute.For<IMapInstance>(), now);

        for (int i = 0; i < 100; i++)
            CharacterReadinessBarrier.ReleaseExpired(connections, world, now, TimeSpan.FromSeconds(15), NullLogger.Instance);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++)
            CharacterReadinessBarrier.ReleaseExpired(connections, world, now, TimeSpan.FromSeconds(15), NullLogger.Instance);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.Null(connection.Character);
        Assert.Equal(waitingForLoad, connection.PendingSpawn is not null);
    }

    [Fact]
    public void Check_an_active_lease_without_allocating_before_its_heartbeat_is_due()
    {
        var clock = new FakeTimeProvider();
        using var connection = WorldAdmissionConnection.Create(clock: clock);
        GameplayTestAdmission.Admit(connection, clock: clock);
        for (int i = 0; i < 100; i++)
            connection.AdvanceGameplayLease();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++)
            connection.AdvanceGameplayLease();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.True(connection.IsGameplayAuthorized);
    }

    [Fact]
    public void Filter_input_without_allocating_and_follow_map_changes_on_the_character_or_row()
    {
        var clock = new FakeTimeProvider();
        using var connection = WorldAdmissionConnection.Create(clock: clock);
        GameplayTestAdmission.Admit(connection, clock: clock);
        CharacterEntity character = TestCharacters.New();
        connection.Character = character;
        var filter = new MapSessionFilter(connection);

        CheckInputFilter(filter, accepted: false);
        character.Map = 2;
        CheckInputFilter(filter, accepted: true);
        character.Data!.Map = 0;
        CheckInputFilter(filter, accepted: false);
        character.Data.Map = 3;
        CheckInputFilter(filter, accepted: true);
        character.Map = 0;
        CheckInputFilter(filter, accepted: false);
        connection.Character = new CharacterEntity();
        CheckInputFilter(filter, accepted: false);
    }

    private static void CheckInputFilter(MapSessionFilter filter, bool accepted)
    {
        for (int i = 0; i < 100; i++)
            filter.CanProcess(NetworkPacketType.CMSG_PLAYER_INPUT);

        int acceptedCount = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++)
        {
            if (filter.CanProcess(NetworkPacketType.CMSG_PLAYER_INPUT))
                acceptedCount++;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(accepted ? 1_000 : 0, acceptedCount);
        Assert.Equal(0, allocated);
    }
}
