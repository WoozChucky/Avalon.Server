using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.World.Maintenance;
using NSubstitute;
using Avalon.Server.World.UnitTests.Loot;

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
        var maintenance = Substitute.For<IWorldMaintenanceRepository>();
        var accounts = Substitute.For<IAccountRepository>();
        maintenance.ReadAsync(new WorldId(1), Arg.Any<CancellationToken>())
            .Returns(new WorldMaintenanceState(enabled, 1,
                deadlinePassed ? clock.Now.UtcDateTime : clock.Now.UtcDateTime.AddMinutes(10)));
        accounts.FindByIdAsync(new AccountId(7), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new Account { Id = new AccountId(7), AccessLevel = access,
                Username = "TEST", Salt = [], Verifier = [], Email = "a@b.com", JoinDate = DateTime.UtcNow });

        var gate = new WorldEntryGate(new WorldId(1), maintenance, accounts, clock);
        WorldEntryDecision decision = await gate.CheckAsync(new AccountId(7), CancellationToken.None);
        Assert.Equal(expected, decision.Allowed);
        if (expected)
            Assert.True(decision.ValidUntilUtc > clock.Now.UtcDateTime);
    }

    [Fact]
    public async Task Refuse_when_state_is_unreadable()
    {
        var maintenance = Substitute.For<IWorldMaintenanceRepository>();
        maintenance.ReadAsync(Arg.Any<WorldId>(), Arg.Any<CancellationToken>())
            .Returns<Task<WorldMaintenanceState?>>(_ => throw new InvalidOperationException("offline"));
        var gate = new WorldEntryGate(new WorldId(1), maintenance, Substitute.For<IAccountRepository>());
        Assert.False((await gate.CheckAsync(new AccountId(7), CancellationToken.None)).Allowed);
    }

    [Fact]
    public async Task Expire_an_open_decision_after_five_seconds_even_without_a_notification()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero));
        var maintenance = Substitute.For<IWorldMaintenanceRepository>();
        var accounts = Substitute.For<IAccountRepository>();
        maintenance.ReadAsync(new WorldId(1), Arg.Any<CancellationToken>())
            .Returns(new WorldMaintenanceState(false, 0, null));
        accounts.FindByIdAsync(new AccountId(7), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new Account { Id = new AccountId(7), AccessLevel = AccountAccessLevel.Player,
                Username = "TEST", Salt = [], Verifier = [], Email = "a@b.com", JoinDate = clock.Now.UtcDateTime });
        var gate = new WorldEntryGate(new WorldId(1), maintenance, accounts, clock);

        WorldEntryDecision decision = await gate.CheckAsync(new AccountId(7), CancellationToken.None);
        clock.Now = clock.Now.AddSeconds(5);

        Assert.True(decision.Allowed);
        Assert.Equal(clock.Now.UtcDateTime, decision.ValidUntilUtc);
    }
}
