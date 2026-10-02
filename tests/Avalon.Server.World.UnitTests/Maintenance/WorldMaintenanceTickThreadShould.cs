using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Network.Packets.Abstractions;
using Avalon.Server.World.UnitTests.Threading;
using Avalon.World.Maintenance;
using Avalon.World.Persistence;
using Avalon.World.Public;
using Avalon.World.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Maintenance;

/// <summary>
/// #639: the maintenance state changes on the tick only. The Redis notification and the reconciliation read the row
/// off the tick and offer it; the next Advance, on the tick, applies it and runs the cutoff it implies.
/// </summary>
public sealed class WorldMaintenanceTickThreadShould
{
    private static readonly DateTime Start = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
    private readonly IWorldMaintenanceRepository _repository = Substitute.For<IWorldMaintenanceRepository>();
    private readonly TickThreadGuard _guard = new();

    public WorldMaintenanceTickThreadShould() => _guard.Bind();

    private WorldMaintenanceCoordinator Coordinator() => new(new WorldId(1), _repository,
        Substitute.For<ICharacterSaver>(), new FixedClock(Start), NullLogger<WorldMaintenanceCoordinator>.Instance,
        Microsoft.Extensions.Options.Options.Create(new Avalon.World.Configuration.WorldShutdownConfiguration()),
        _guard);

    [Fact]
    public async Task Apply_a_notification_read_off_the_tick_only_on_the_next_tick()
    {
        WorldMaintenanceCoordinator coordinator = Coordinator();
        IWorldConnection player = Connection(AccountAccessLevel.Player);
        var enabled = new WorldMaintenanceState(true, 2, Start.AddMinutes(5));
        _repository.ReadAsync(new WorldId(1), Arg.Any<CancellationToken>()).Returns(enabled);

        await Task.Run(() => coordinator.ApplyNotificationAsync(2, CancellationToken.None));
        _guard.Bind(); // the tick is this test's thread, wherever the await resumed

        Assert.Null(coordinator.CurrentState);
        player.DidNotReceiveWithAnyArgs().Send(default!);

        coordinator.Advance(Start, [player]);

        Assert.Equal(enabled, coordinator.CurrentState);
        player.Received(1).Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public async Task Run_a_cutoff_found_by_reconciliation_on_the_tick()
    {
        WorldMaintenanceCoordinator coordinator = Coordinator();
        IWorldConnection player = Connection(AccountAccessLevel.Player);
        _repository.ReadAsync(new WorldId(1), Arg.Any<CancellationToken>())
            .Returns(new WorldMaintenanceState(true, 1, Start.AddSeconds(-1)));

        await Task.Run(() => coordinator.ReconcileAsync(CancellationToken.None));
        _guard.Bind(); // the tick is this test's thread, wherever the await resumed

        await player.DidNotReceive().CloseAsync();
        ((IMaintenanceBlockable)player).DidNotReceive().BlockForMaintenance();

        coordinator.Advance(Start, [player]);

        await player.Received(1).CloseAsync();
        ((IMaintenanceBlockable)player).Received().BlockForMaintenance();
    }

    [Fact]
    public void Keep_the_newest_offered_revision_whatever_order_they_arrive_in()
    {
        WorldMaintenanceCoordinator coordinator = Coordinator();
        var newer = new WorldMaintenanceState(false, 5, null);

        Assert.Null(TickThreadGuardProbe.OffThread(() => coordinator.Offer(newer)));
        Assert.Null(TickThreadGuardProbe.OffThread(() =>
            coordinator.Offer(new WorldMaintenanceState(true, 4, Start.AddMinutes(5)))));
        coordinator.Advance(Start, []);

        Assert.Equal(newer, coordinator.CurrentState);
    }

    [Fact]
    public void Refuse_to_change_the_applied_state_off_the_tick()
    {
        WorldMaintenanceCoordinator coordinator = Coordinator();
        var state = new WorldMaintenanceState(true, 1, Start.AddMinutes(5));

        AssertRefusedOffTick(() => coordinator.ApplyCommitted(state), "WorldMaintenanceCoordinator.ApplyCommitted");
        AssertRefusedOffTick(() => coordinator.Advance(Start, []), "WorldMaintenanceCoordinator.Advance");
        AssertRefusedOffTick(() => coordinator.RunIfEntryAllowed(Connection(AccountAccessLevel.Player),
            new WorldEntryDecision(true, DateTime.MaxValue), () => { }), "WorldMaintenanceCoordinator.RunIfEntryAllowed");
        Assert.Null(coordinator.CurrentState);

        coordinator.ApplyCommitted(state);
        Assert.Equal(state, coordinator.CurrentState);
    }

    private static void AssertRefusedOffTick(Action call, string operation)
    {
        var refused = Assert.IsType<InvalidOperationException>(TickThreadGuardProbe.OffThread(call));
        Assert.Contains(operation, refused.Message, StringComparison.Ordinal);
    }

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

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}
