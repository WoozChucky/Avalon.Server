using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Social;
using Avalon.Network.Packets.Generic;
using Avalon.World.Maintenance;
using Avalon.World.Persistence;
using Avalon.World.Public;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ProtoBuf;

namespace Avalon.Server.World.UnitTests.Maintenance;

public sealed class WorldMaintenanceCoordinatorShould
{
    private static readonly DateTime Start = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
    private readonly IWorldMaintenanceRepository _repository = Substitute.For<IWorldMaintenanceRepository>();
    private readonly ICharacterSaver _saver = Substitute.For<ICharacterSaver>();

    private WorldMaintenanceCoordinator Coordinator() => new(new WorldId(1), _repository, _saver,
        new FixedClock(Start), NullLogger<WorldMaintenanceCoordinator>.Instance);

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
        player.Received().BlockForMaintenance();
        admin.DidNotReceive().CloseAsync();
        admin.DidNotReceive().BlockForMaintenance();
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
        connecting.DidNotReceive().BlockForMaintenance();

        connecting.AccountId.Returns(new AccountId(42));
        connecting.AccessLevel.Returns(AccountAccessLevel.Admin);
        coordinator.Advance(Start.AddSeconds(1), [connecting]);
        connecting.DidNotReceive().CloseAsync();
        connecting.DidNotReceive().BlockForMaintenance();

        connecting.AccessLevel.Returns(AccountAccessLevel.Player);
        coordinator.Advance(Start.AddSeconds(2), [connecting]);
        connecting.Received(1).CloseAsync();
        connecting.Received().BlockForMaintenance();
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
        player.DidNotReceive().CloseAsync();
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
        player.DidNotReceive().CloseAsync();
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
        player.DidNotReceive().CloseAsync();
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
        player.Received().BlockForMaintenance();
        Task drained = coordinator.WhenDrainedAsync(CancellationToken.None);
        Assert.False(drained.IsCompleted);
        save.SetResult();
        await drained;
    }

    private static IWorldConnection Connection(AccountAccessLevel access)
    {
        var connection = Substitute.For<IWorldConnection>();
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
