using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.World.Maintenance;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Maintenance;

public sealed class WorldEntryGateShould
{
    [Theory]
    [InlineData(AccountAccessLevel.Player, true, false)]
    [InlineData(AccountAccessLevel.GameMaster, true, false)]
    [InlineData(AccountAccessLevel.Admin, true, true)]
    [InlineData(AccountAccessLevel.Player, false, true)]
    public async Task Respect_maintenance_and_Admin_flag(AccountAccessLevel access, bool enabled, bool expected)
    {
        var maintenance = Substitute.For<IWorldMaintenanceRepository>();
        var accounts = Substitute.For<IAccountRepository>();
        maintenance.ReadAsync(new WorldId(1), Arg.Any<CancellationToken>())
            .Returns(new WorldMaintenanceState(enabled, 1, DateTime.UtcNow.AddMinutes(5)));
        accounts.FindByIdAsync(new AccountId(7), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new Account { Id = new AccountId(7), AccessLevel = access,
                Username = "TEST", Salt = [], Verifier = [], Email = "a@b.com", JoinDate = DateTime.UtcNow });

        var gate = new WorldEntryGate(new WorldId(1), maintenance, accounts);
        Assert.Equal(expected, await gate.CheckAsync(new AccountId(7), CancellationToken.None));
    }

    [Fact]
    public async Task Refuse_when_state_is_unreadable()
    {
        var maintenance = Substitute.For<IWorldMaintenanceRepository>();
        maintenance.ReadAsync(Arg.Any<WorldId>(), Arg.Any<CancellationToken>())
            .Returns<Task<WorldMaintenanceState?>>(_ => throw new InvalidOperationException("offline"));
        var gate = new WorldEntryGate(new WorldId(1), maintenance, Substitute.For<IAccountRepository>());
        Assert.False(await gate.CheckAsync(new AccountId(7), CancellationToken.None));
    }
}
