using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure.WorldMaintenance;
using Avalon.Network.Packets.Social;
using Avalon.World.Chat;
using Avalon.World.Maintenance;
using Avalon.World.Persistence;
using Avalon.World.Public;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Chat;

public sealed class MaintenanceCommandShould
{
    [Fact]
    public async Task Admin_can_enable_disable_and_read_persisted_state()
    {
        var f = new Fixture(AccountAccessLevel.Admin);
        DateTime deadline = DateTime.UtcNow.AddMinutes(5);
        var enabled = new WorldMaintenanceState(true, 1, deadline);
        var disabled = new WorldMaintenanceState(false, 2, null);
        f.Control.SetAsync(new WorldId(1), true, TimeSpan.FromMinutes(5), Arg.Any<string>(),
            Arg.Any<CancellationToken>()).Returns(enabled);
        f.Control.SetAsync(new WorldId(1), false, Arg.Any<TimeSpan>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>()).Returns(disabled);
        f.Repository.ReadAsync(new WorldId(1), Arg.Any<CancellationToken>()).Returns(enabled);

        Assert.True(await f.Dispatch("/maintenance on"));
        Assert.Equal(enabled, f.Coordinator.CurrentState);
        Assert.Contains("revision 1", f.LastMessage());
        Assert.True(await f.Dispatch("/maintenance status"));
        Assert.Contains("revision 1", f.LastMessage());
        Assert.True(await f.Dispatch("/maintenance off"));
        Assert.Equal(disabled, f.Coordinator.CurrentState);
        Assert.Contains("revision 2", f.LastMessage());
    }

    [Fact]
    public async Task Repeated_enable_reports_the_original_deadline()
    {
        var f = new Fixture(AccountAccessLevel.Admin);
        var state = new WorldMaintenanceState(true, 3, DateTime.UtcNow.AddMinutes(5));
        f.Control.SetAsync(new WorldId(1), true, TimeSpan.FromMinutes(10), Arg.Any<string>(),
            Arg.Any<CancellationToken>()).Returns(state);

        Assert.True(await f.Dispatch("/maintenance on 10"));
        string first = f.LastMessage();
        Assert.True(await f.Dispatch("/maintenance on 10"));

        Assert.Equal(first, f.LastMessage());
        Assert.Equal(3, f.Coordinator.CurrentState!.Revision);
    }

    [Fact]
    public async Task Failed_write_reports_failure_and_keeps_local_state()
    {
        var f = new Fixture(AccountAccessLevel.Admin);
        var prior = new WorldMaintenanceState(true, 4, DateTime.UtcNow.AddMinutes(5));
        f.Coordinator.ApplyCommitted(prior);
        f.Control.SetAsync(new WorldId(1), false, Arg.Any<TimeSpan>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<WorldMaintenanceState?>>(_ => throw new InvalidOperationException("database offline"));

        Assert.True(await f.Dispatch("/maintenance off"));

        Assert.Equal(prior, f.Coordinator.CurrentState);
        Assert.Contains("failed", f.LastMessage());
    }

    [Theory]
    [InlineData(AccountAccessLevel.Player)]
    [InlineData(AccountAccessLevel.GameMaster)]
    [InlineData(AccountAccessLevel.Console)]
    public async Task Hide_the_command_from_non_Admin_flags(AccountAccessLevel access)
    {
        var f = new Fixture(access);
        Assert.False(await f.Dispatch("/maintenance on"));
        Assert.Empty(f.Messages());
        await f.Control.DidNotReceiveWithAnyArgs().SetAsync(default!, default, default, default!, default);
    }

    [Theory]
    [InlineData("/maintenance on 0")]
    [InlineData("/maintenance on 61")]
    [InlineData("/maintenance on x")]
    [InlineData("/maintenance on 5 extra")]
    public async Task Reject_invalid_grace_without_a_write(string command)
    {
        var f = new Fixture(AccountAccessLevel.Admin);
        Assert.True(await f.Dispatch(command));
        Assert.Contains("Usage", f.LastMessage());
        await f.Control.DidNotReceiveWithAnyArgs().SetAsync(default!, default, default, default!, default);
    }

    [Fact]
    public void Write_off_the_tick_and_apply_the_committed_state_only_in_the_continuation()
    {
        var connection = Substitute.For<IWorldConnection>();
        connection.AccountId.Returns(new AccountId(7));
        connection.AccessLevel.Returns(AccountAccessLevel.Admin);
        connection.CryptoSession.Returns(new FakeAvalonCryptoSession());
        var queued = new List<(Task Task, Action Callback)>();
        connection.When(c => c.EnqueueContinuation(Arg.Any<Task>(), Arg.Any<Action>()))
            .Do(ci => queued.Add((ci.Arg<Task>(), ci.Arg<Action>())));
        var repository = Substitute.For<IWorldMaintenanceRepository>();
        var control = Substitute.For<IWorldMaintenanceControl>();
        var committed = new WorldMaintenanceState(true, 1, DateTime.UtcNow.AddMinutes(5));
        control.SetAsync(new WorldId(1), true, TimeSpan.FromMinutes(5), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(committed);
        var coordinator = new WorldMaintenanceCoordinator(new WorldId(1), repository,
            Substitute.For<ICharacterSaver>(), TimeProvider.System, NullLogger<WorldMaintenanceCoordinator>.Instance);
        var dispatcher = new CommandDispatcher([new MaintenanceCommand(new WorldId(1), repository, control, coordinator)],
            NullLogger<CommandDispatcher>.Instance);

        Assert.True(dispatcher.Dispatch(connection, new CChatMessagePacket { Message = "/maintenance on", DateTime = DateTime.UtcNow }));

        // Dispatch returned without waiting for the write, and nothing is applied until a tick runs the callback.
        Assert.Single(queued);
        Assert.Null(coordinator.CurrentState);

        Assert.True(queued[0].Task.Wait(TimeSpan.FromSeconds(5)));
        queued[0].Callback();

        Assert.Equal(committed, coordinator.CurrentState);
    }

    private sealed class Fixture
    {
        private readonly CommandConnection _caller;
        private readonly CommandDispatcher _dispatcher;

        public readonly IWorldMaintenanceRepository Repository = Substitute.For<IWorldMaintenanceRepository>();
        public readonly IWorldMaintenanceControl Control = Substitute.For<IWorldMaintenanceControl>();
        public readonly WorldMaintenanceCoordinator Coordinator;

        public Fixture(AccountAccessLevel access)
        {
            _caller = new CommandConnection(access);
            _caller.Connection.AccountId.Returns(new AccountId(7));
            Coordinator = new WorldMaintenanceCoordinator(new WorldId(1), Repository,
                Substitute.For<ICharacterSaver>(), TimeProvider.System,
                NullLogger<WorldMaintenanceCoordinator>.Instance);
            _dispatcher = new CommandDispatcher(
                [new MaintenanceCommand(new WorldId(1), Repository, Control, Coordinator)],
                NullLogger<CommandDispatcher>.Instance);
        }

        // The connection runs each continuation inline, as the tick would a tick later.
        public Task<bool> Dispatch(string message) => Task.FromResult(_dispatcher.Dispatch(_caller.Connection,
            new CChatMessagePacket { Message = message, DateTime = DateTime.UtcNow }));

        public List<string> Messages() => _caller.Messages();

        public string LastMessage() => Messages().Last();
    }
}
