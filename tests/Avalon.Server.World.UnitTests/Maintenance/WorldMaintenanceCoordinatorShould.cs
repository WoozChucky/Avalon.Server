using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Social;
using Avalon.Network.Packets.Generic;
using Avalon.World.Configuration;
using Avalon.World.Maintenance;
using Avalon.World.Persistence;
using Avalon.World.Public;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using ProtoBuf;

namespace Avalon.Server.World.UnitTests.Maintenance;

public sealed class WorldMaintenanceCoordinatorShould
{
    private static readonly DateTime Start = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
    private readonly IWorldMaintenanceRepository _repository = Substitute.For<IWorldMaintenanceRepository>();
    private readonly ICharacterSaver _saver = Substitute.For<ICharacterSaver>();

    private WorldMaintenanceCoordinator Coordinator(TimeSpan drainTime = default) => new(new WorldId(1), _repository,
        _saver, new FixedClock(Start), NullLogger<WorldMaintenanceCoordinator>.Instance,
        Options.Create(new WorldShutdownConfiguration { DrainTime = drainTime, SaveMargin = TimeSpan.FromMinutes(1) }));

    [Fact]
    public void Announce_relevant_thresholds_once_and_disconnect_non_Admins_after_zero()
    {
        var coordinator = Coordinator();
        var player = Connection(AccountAccessLevel.Player);
        var admin = Connection(AccountAccessLevel.Admin);
        coordinator.ApplyCommitted(new WorldMaintenanceState(true, 1, Start.AddMinutes(5)));

        coordinator.Advance(Start, [player, admin]);
        coordinator.Advance(Start.AddMinutes(2), [player, admin]);
        coordinator.Advance(Start.AddMinutes(4), [player, admin]);
        coordinator.Advance(Start.AddMinutes(4).AddSeconds(30), [player, admin]);
        for (int second = 10; second >= 0; second--)
            coordinator.Advance(Start.AddMinutes(5).AddSeconds(-second), [player, admin]);
        coordinator.Advance(Start.AddMinutes(5).AddSeconds(1), [player, admin]);

        string[] messages = Messages(player);
        Assert.Equal(15, messages.Length); // start, 3m, 1m, 30s, 10..0
        Assert.Equal(messages, Messages(admin));
        Assert.Contains("3 minutes", messages[1]);
        Assert.Contains("0 seconds", messages[^1]);
        player.Received(1).CloseAsync();
        ((IMaintenanceBlockable)player).Received().BlockForMaintenance();
        admin.DidNotReceive().CloseAsync();
        ((IMaintenanceBlockable)admin).DidNotReceive().BlockForMaintenance();
    }

    [Fact]
    public void Leave_a_new_socket_time_to_authenticate_after_the_deadline()
    {
        var coordinator = Coordinator();
        var connecting = Connection(AccountAccessLevel.Player);
        connecting.AccountId.Returns((AccountId?)null);
        coordinator.ApplyCommitted(new WorldMaintenanceState(true, 1, Start.AddSeconds(-1)));

        coordinator.Advance(Start, [connecting]);
        connecting.DidNotReceive().CloseAsync();
        ((IMaintenanceBlockable)connecting).DidNotReceive().BlockForMaintenance();

        connecting.AccountId.Returns(new AccountId(42));
        connecting.AccessLevel.Returns(AccountAccessLevel.Admin);
        coordinator.Advance(Start.AddSeconds(1), [connecting]);
        connecting.DidNotReceive().CloseAsync();
        ((IMaintenanceBlockable)connecting).DidNotReceive().BlockForMaintenance();

        connecting.AccessLevel.Returns(AccountAccessLevel.Player);
        coordinator.Advance(Start.AddSeconds(2), [connecting]);
        connecting.Received(1).CloseAsync();
        ((IMaintenanceBlockable)connecting).Received().BlockForMaintenance();
    }

    [Fact]
    public async Task Ignore_old_notifications_and_do_not_replay_start_after_restart()
    {
        var coordinator = Coordinator();
        var player = Connection(AccountAccessLevel.Player);
        _repository.ReadAsync(new WorldId(1), Arg.Any<CancellationToken>())
            .Returns(new WorldMaintenanceState(true, 3, Start.AddMinutes(5)));
        await coordinator.InitializeAsync(CancellationToken.None);
        coordinator.Advance(Start, [player]);
        Assert.Empty(Messages(player));

        coordinator.ApplyCommitted(new WorldMaintenanceState(false, 4, null));
        await coordinator.ApplyNotificationAsync(3, CancellationToken.None);
        coordinator.Advance(Start.AddMinutes(5), [player]);
        Assert.Empty(Messages(player));
        _ = player.DidNotReceive().CloseAsync();
    }

    [Fact]
    public async Task Reload_a_newer_notification_from_the_database()
    {
        var coordinator = Coordinator();
        var player = Connection(AccountAccessLevel.Player);
        coordinator.ApplyCommitted(new WorldMaintenanceState(true, 1, Start.AddMinutes(5)));
        _repository.ReadAsync(new WorldId(1), Arg.Any<CancellationToken>())
            .Returns(new WorldMaintenanceState(false, 2, null));

        await coordinator.ApplyNotificationAsync(2, CancellationToken.None);
        coordinator.Advance(Start.AddMinutes(5), [player]);

        Assert.False(coordinator.CurrentState!.Enabled);
        _ = player.DidNotReceive().CloseAsync();
    }

    [Fact]
    public void Disable_cancels_countdown_and_a_delayed_tick_skips_old_warnings()
    {
        var coordinator = Coordinator();
        var player = Connection(AccountAccessLevel.Player);
        coordinator.ApplyCommitted(new WorldMaintenanceState(true, 1, Start.AddMinutes(5)));
        coordinator.Advance(Start, [player]);
        coordinator.Advance(Start.AddMinutes(4).AddSeconds(50), [player]);
        Assert.Equal(2, Messages(player).Length);
        Assert.Contains("10 seconds", Messages(player)[1]);

        coordinator.ApplyCommitted(new WorldMaintenanceState(false, 2, null));
        coordinator.Advance(Start.AddMinutes(5), [player]);

        Assert.Equal(2, Messages(player).Length);
        _ = player.DidNotReceive().CloseAsync();
    }

    [Fact]
    public async Task Late_discovery_closes_with_zero_chat_before_disconnect_and_waits_for_saves()
    {
        var coordinator = Coordinator();
        var player = Connection(AccountAccessLevel.Player);
        var save = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _saver.WhenAllIdle().Returns(save.Task);
        _repository.ReadAsync(new WorldId(1), Arg.Any<CancellationToken>())
            .Returns(new WorldMaintenanceState(true, 3, Start.AddSeconds(-1)));
        await coordinator.InitializeAsync(CancellationToken.None);

        coordinator.Advance(Start, [player]);

        var packets = player.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IWorldConnection.Send))
            .Select(call => call.GetArguments()[0]).OfType<NetworkPacket>().ToArray();
        Assert.Equal(NetworkPacketType.SMSG_CHAT_MESSAGE, packets[0].Header.Type);
        Assert.Equal(NetworkPacketType.SMSG_DISCONNECT, packets[1].Header.Type);
        Assert.Equal(DisconnectReason.Maintenance,
            Serializer.Deserialize<SDisconnectPacket>(new MemoryStream(packets[1].Payload)).ReasonCode);
        ((IMaintenanceBlockable)player).Received().BlockForMaintenance();
        Task drained = coordinator.WhenDrainedAsync(CancellationToken.None);
        Assert.False(drained.IsCompleted);
        save.SetResult();
        await drained;
    }

    [Fact]
    public void Forget_connections_closed_earlier_in_a_long_cutoff()
    {
        var coordinator = Coordinator();
        var first = Connection(AccountAccessLevel.Player);
        var second = Connection(AccountAccessLevel.Player);
        coordinator.ApplyCommitted(new WorldMaintenanceState(true, 1, Start.AddSeconds(-1)));

        coordinator.Advance(Start, [first]);
        Assert.Contains(first, Closing(coordinator));

        first.IsConnected.Returns(false);
        coordinator.Advance(Start.AddSeconds(1), [second]);

        Assert.DoesNotContain(first, Closing(coordinator));
        Assert.Contains(second, Closing(coordinator));
        _ = first.Received(1).CloseAsync();
    }

    // The restart drain (#768): a stop warns on the maintenance schedule, in memory only, and ends at its deadline.
    [Fact]
    public async Task Warn_of_a_restart_on_the_schedule_and_end_the_drain_at_its_deadline()
    {
        var coordinator = Coordinator(TimeSpan.FromMinutes(5));
        var player = Connection(AccountAccessLevel.Player);
        var admin = Connection(AccountAccessLevel.Admin);

        Task drain = coordinator.DrainForRestartAsync(CancellationToken.None);
        coordinator.Advance(Start, [player, admin]);
        coordinator.Advance(Start.AddMinutes(2), [player, admin]);
        coordinator.Advance(Start.AddMinutes(4), [player, admin]);
        coordinator.Advance(Start.AddMinutes(4).AddSeconds(30), [player, admin]);
        for (int second = 10; second >= 1; second--)
            coordinator.Advance(Start.AddMinutes(5).AddSeconds(-second), [player, admin]);
        Assert.False(drain.IsCompleted, "the drain ended before its deadline with a player still in the world");
        ((IMaintenanceBlockable)player).DidNotReceive().BlockForMaintenance();

        coordinator.Advance(Start.AddMinutes(5), [player, admin]);

        await drain.WaitAsync(TimeSpan.FromSeconds(5));
        string[] messages = Messages(player);
        Assert.Equal(15, messages.Length); // start, 3m, 1m, 30s, 10..1, now
        Assert.Equal(messages, Messages(admin));
        Assert.Equal("The world restarts for an update in 5 minutes.", messages[0]);
        Assert.Equal("The world restarts for an update in 3 minutes.", messages[1]);
        Assert.Equal("The world restarts for an update in 1 second.", messages[^2]);
        Assert.Equal("Restarting now.", messages[^1]);
        // The stop that follows closes everyone with the shutdown reason; the drain only stops their packets.
        ((IMaintenanceBlockable)player).Received().BlockForMaintenance();
        ((IMaintenanceBlockable)admin).DidNotReceive().BlockForMaintenance();
        _ = player.DidNotReceive().CloseAsync();
    }

    [Fact]
    public async Task End_the_restart_drain_once_no_non_Admin_player_is_left()
    {
        var coordinator = Coordinator(TimeSpan.FromMinutes(5));
        var player = Connection(AccountAccessLevel.Player);
        var admin = Connection(AccountAccessLevel.Admin);
        var connecting = Connection(AccountAccessLevel.Player);
        connecting.AccountId.Returns((AccountId?)null);

        Task drain = coordinator.DrainForRestartAsync(CancellationToken.None);
        coordinator.Advance(Start, [player, admin, connecting]);
        Assert.False(drain.IsCompleted);

        player.IsConnected.Returns(false);
        coordinator.Advance(Start.AddSeconds(1), [player, admin, connecting]);

        await drain.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// With persisted maintenance already past its cutoff only Admins are left, so the drain ends on its first tick.
    /// The restart is never written or published, and a persisted state applied during it neither replaces it nor is
    /// replaced by it.
    /// </summary>
    [Fact]
    public async Task Keep_the_restart_in_memory_beside_the_persisted_state()
    {
        var coordinator = Coordinator(TimeSpan.FromMinutes(5));
        var player = Connection(AccountAccessLevel.Player);
        coordinator.ApplyCommitted(new WorldMaintenanceState(false, 3, null));

        Task drain = coordinator.DrainForRestartAsync(CancellationToken.None);
        coordinator.Advance(Start, [player]);
        coordinator.Offer(new WorldMaintenanceState(true, 4, Start.AddMinutes(10)));
        coordinator.Advance(Start.AddMinutes(4), [player]);
        coordinator.ApplyCommitted(new WorldMaintenanceState(false, 5, null));
        coordinator.Advance(Start.AddMinutes(4).AddSeconds(30), [player]);
        Assert.False(drain.IsCompleted, "a persisted state replaced the restart");

        coordinator.Advance(Start.AddMinutes(5), [player]);

        await drain.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new WorldMaintenanceState(false, 5, null), coordinator.CurrentState);
        Assert.Equal("Restarting now.", Messages(player)[^1]);
        Assert.DoesNotContain(_repository.ReceivedCalls(), call =>
            call.GetMethodInfo().Name == nameof(IWorldMaintenanceRepository.TransitionAsync));
    }

    [Fact]
    public async Task End_the_restart_drain_at_once_when_maintenance_left_only_Admins()
    {
        var coordinator = Coordinator(TimeSpan.FromMinutes(5));
        var admin = Connection(AccountAccessLevel.Admin);
        coordinator.ApplyCommitted(new WorldMaintenanceState(true, 1, Start.AddMinutes(-1)));

        Task drain = coordinator.DrainForRestartAsync(CancellationToken.None);
        coordinator.Advance(Start, [admin]);

        await drain.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Not_drain_when_the_drain_time_is_zero()
    {
        var coordinator = Coordinator(TimeSpan.Zero);
        var player = Connection(AccountAccessLevel.Player);

        await coordinator.DrainForRestartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        coordinator.Advance(Start, [player]);

        Assert.Empty(Messages(player));
        ((IMaintenanceBlockable)player).DidNotReceive().BlockForMaintenance();
    }

    [Fact]
    public void Refuse_a_non_Admin_entry_once_the_restart_deadline_has_passed()
    {
        var coordinator = Coordinator(TimeSpan.FromSeconds(10));
        var player = Connection(AccountAccessLevel.Player);
        var admin = Connection(AccountAccessLevel.Admin);
        coordinator.ApplyCommitted(new WorldMaintenanceState(false, 1, null));
        var decision = new WorldEntryDecision(true, DateTime.MaxValue);

        _ = coordinator.DrainForRestartAsync(CancellationToken.None);
        coordinator.Advance(Start, [player, admin]);
        Assert.True(coordinator.RunIfEntryAllowed(player, decision, () => { }));

        coordinator.Advance(Start.AddSeconds(10), [player, admin]);
        Assert.False(coordinator.RunIfEntryAllowed(player, decision, () => { }));
        Assert.True(coordinator.RunIfEntryAllowed(admin, decision, () => { }));
    }

    private static HashSet<IWorldConnection> Closing(WorldMaintenanceCoordinator coordinator) =>
        (HashSet<IWorldConnection>)typeof(WorldMaintenanceCoordinator)
            .GetField("_closing", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(coordinator)!;

    private static IWorldConnection Connection(AccountAccessLevel access)
    {
        var connection = Substitute.For<IWorldConnection, IMaintenanceBlockable>();
        connection.AccountId.Returns(new AccountId(42));
        connection.AccessLevel.Returns(access);
        connection.IsConnected.Returns(true);
        connection.InGame.Returns(true);
        connection.CryptoSession.Returns(new FakeAvalonCryptoSession());
        connection.CloseAsync().Returns(Task.CompletedTask);
        return connection;
    }

    private static string[] Messages(IWorldConnection connection) => connection.ReceivedCalls()
        .Where(call => call.GetMethodInfo().Name == nameof(IWorldConnection.Send))
        .Select(call => call.GetArguments()[0]).OfType<NetworkPacket>()
        .Where(packet => packet.Header.Type == NetworkPacketType.SMSG_CHAT_MESSAGE)
        .Select(packet => Serializer.Deserialize<SChatMessagePacket>(new MemoryStream(packet.Payload)).Message)
        .ToArray();

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}
