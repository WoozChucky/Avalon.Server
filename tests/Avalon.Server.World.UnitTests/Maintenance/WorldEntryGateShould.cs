using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.World.Maintenance;
using Avalon.World.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.Core;

namespace Avalon.Server.World.UnitTests.Maintenance;

public sealed class WorldEntryGateShould
{
    [Theory]
    [InlineData(AccountAccessLevel.Player, true, false, true)]
    [InlineData(AccountAccessLevel.GameMaster, true, false, true)]
    [InlineData(AccountAccessLevel.Admin, true, false, true)]
    [InlineData(AccountAccessLevel.Player, true, true, false)]
    [InlineData(AccountAccessLevel.Admin, true, true, true)]
    [InlineData(AccountAccessLevel.Player, false, false, true)]
    public async Task Respect_deadline_and_Admin_flag(AccountAccessLevel access, bool enabled,
        bool deadlinePassed, bool expected)
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero));
        IWorldMaintenanceRepository maintenance = Substitute.For<IWorldMaintenanceRepository>();
        IAccountRepository accounts = Substitute.For<IAccountRepository>();
        maintenance.ReadAsync(new WorldId(1), Arg.Any<CancellationToken>())
            .Returns(new WorldMaintenanceState(enabled, 1,
                deadlinePassed ? clock.Now.UtcDateTime : clock.Now.UtcDateTime.AddMinutes(10)));
        accounts.FindByIdAsync(new AccountId(7), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new Account
            {
                Id = new AccountId(7),
                AccessLevel = access,
                Username = "TEST",
                Salt = [],
                Verifier = [],
                Email = "a@b.com",
                JoinDate = DateTime.UtcNow
            });

        var gate = new WorldEntryGate(new WorldId(1), maintenance, accounts, clock);
        WorldEntryDecision decision = await gate.CheckAsync(new AccountId(7), CancellationToken.None);
        Assert.Equal(expected, decision.Allowed);
        if (expected)
            Assert.True(decision.ValidUntilUtc > clock.Now.UtcDateTime);
    }

    [Fact]
    public async Task Refuse_when_state_is_unreadable_and_log_why_by_type_once_per_interval()
    {
        IWorldMaintenanceRepository maintenance = Substitute.For<IWorldMaintenanceRepository>();
        maintenance.ReadAsync(Arg.Any<WorldId>(), Arg.Any<CancellationToken>())
            .Returns<Task<WorldMaintenanceState?>>(_ => throw new InvalidOperationException("offline at db.internal"));
        ILogger<WorldEntryGate> logger = Substitute.For<ILogger<WorldEntryGate>>();
        var gate = new WorldEntryGate(new WorldId(1), maintenance, Substitute.For<IAccountRepository>(),
            new FixedTimeProvider(DateTimeOffset.UnixEpoch), logger);

        Assert.False((await gate.CheckAsync(new AccountId(7), CancellationToken.None)).Allowed);
        Assert.False((await gate.CheckAsync(new AccountId(7), CancellationToken.None)).Allowed);

        ICall logged = Assert.Single(logger.ReceivedCalls(), c => c.GetMethodInfo().Name == nameof(ILogger.Log));
        Assert.Equal(LogLevel.Error, logged.GetArguments()[0]);
        string message = logged.GetArguments()[2]!.ToString()!;
        Assert.Contains(nameof(InvalidOperationException), message, StringComparison.Ordinal);
        Assert.DoesNotContain("db.internal", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Answer_a_check_the_saturated_work_queue_refused_as_a_refusal_not_a_fault()
    {
        IWorldEntryGate gate = Substitute.For<IWorldEntryGate>();
        gate.CheckAsync(Arg.Any<AccountId>(), Arg.Any<CancellationToken>()).Returns(new WorldEntryDecision(true, DateTime.MaxValue));
        var saturated = new WorldDatabaseWork(maximumConcurrency: 1, maximumOutstanding: 1);
        var blocker = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int> holding = saturated.Run(() => blocker.Task);

        Task<WorldEntryDecision> check = gate.CheckOffTick(new AccountId(7), NullLogger.Instance, saturated);

        Assert.True(check.IsCompletedSuccessfully);
        Assert.False((await check).Allowed);
        blocker.SetResult(0);
        await holding;
    }

    [Fact]
    public async Task Expire_an_open_decision_after_five_seconds_even_without_a_notification()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero));
        IWorldMaintenanceRepository maintenance = Substitute.For<IWorldMaintenanceRepository>();
        IAccountRepository accounts = Substitute.For<IAccountRepository>();
        maintenance.ReadAsync(new WorldId(1), Arg.Any<CancellationToken>())
            .Returns(new WorldMaintenanceState(false, 0, null));
        accounts.FindByIdAsync(new AccountId(7), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new Account
            {
                Id = new AccountId(7),
                AccessLevel = AccountAccessLevel.Player,
                Username = "TEST",
                Salt = [],
                Verifier = [],
                Email = "a@b.com",
                JoinDate = clock.Now.UtcDateTime
            });
        var gate = new WorldEntryGate(new WorldId(1), maintenance, accounts, clock);

        WorldEntryDecision decision = await gate.CheckAsync(new AccountId(7), CancellationToken.None);
        clock.Now = clock.Now.AddSeconds(5);

        Assert.True(decision.Allowed);
        Assert.Equal(clock.Now.UtcDateTime, decision.ValidUntilUtc);
    }
}
