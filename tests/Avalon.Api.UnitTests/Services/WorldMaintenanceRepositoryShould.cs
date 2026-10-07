using Avalon.Database.Auth.Migrations;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Avalon.Api.UnitTests.Services;

public sealed class WorldMaintenanceRepositoryShould : IDisposable
{
    private readonly SqliteAuthDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task Start_maintenance_with_a_persisted_five_minute_deadline()
    {
        var repository = new WorldMaintenanceRepository(_database);
        var now = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

        WorldMaintenanceState? state = await repository.TransitionAsync(
            new WorldId(1), true, TimeSpan.FromMinutes(5), now, CancellationToken.None);

        Assert.Equal(new WorldMaintenanceState(true, 1, now.AddMinutes(5)), state);
        Assert.Equal(state, await repository.ReadAsync(new WorldId(1), CancellationToken.None));
    }

    [Fact]
    public async Task Repeated_enable_keeps_the_first_deadline_and_revision()
    {
        var repository = new WorldMaintenanceRepository(_database);
        var noon = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
        var first = await repository.TransitionAsync(new WorldId(1), true, TimeSpan.FromMinutes(5), noon,
            CancellationToken.None);

        var repeated = await repository.TransitionAsync(new WorldId(1), true, TimeSpan.FromMinutes(30),
            noon.AddSeconds(15), CancellationToken.None);

        Assert.Equal(new WorldMaintenanceState(true, 1, noon.AddMinutes(5)), first);
        Assert.Equal(first, repeated);
        Assert.Equal(first, await repository.ReadAsync(new WorldId(1), CancellationToken.None));
    }

    [Fact]
    public async Task Concurrent_enables_commit_only_one_transition()
    {
        var repository = new WorldMaintenanceRepository(_database);
        var noon = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

        WorldMaintenanceState?[] results = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => repository.TransitionAsync(new WorldId(1), true,
                TimeSpan.FromMinutes(5), noon, CancellationToken.None)));

        Assert.All(results, result => Assert.Equal(new WorldMaintenanceState(true, 1, noon.AddMinutes(5)), result));
        Assert.Equal(results[0], await repository.ReadAsync(new WorldId(1), CancellationToken.None));
    }

    [Fact]
    public async Task Disable_clears_the_deadline_and_advances_revision_once()
    {
        var repository = new WorldMaintenanceRepository(_database);
        var noon = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
        await repository.TransitionAsync(new WorldId(1), true, TimeSpan.FromMinutes(5), noon,
            CancellationToken.None);

        var off = await repository.TransitionAsync(new WorldId(1), false, TimeSpan.Zero,
            noon.AddMinutes(2), CancellationToken.None);
        var repeated = await repository.TransitionAsync(new WorldId(1), false, TimeSpan.Zero,
            noon.AddMinutes(3), CancellationToken.None);

        Assert.Equal(new WorldMaintenanceState(false, 2, null), off);
        Assert.Equal(off, repeated);
    }

    [Fact]
    public async Task Missing_world_returns_null_without_creating_state()
    {
        var repository = new WorldMaintenanceRepository(_database);
        var missing = new WorldId(555);

        Assert.Null(await repository.ReadAsync(missing, CancellationToken.None));
        Assert.Null(await repository.TransitionAsync(missing, true, TimeSpan.FromMinutes(5),
            DateTime.UtcNow, CancellationToken.None));
    }

    [Fact]
    public void Migration_preserves_legacy_maintenance_worlds()
    {
        var migration = new AddWorldMaintenance();

        var dataConversion = Assert.Single(migration.UpOperations.OfType<SqlOperation>());
        Assert.Contains("WHERE \"Status\" = 2", dataConversion.Sql, StringComparison.Ordinal);
        Assert.Contains("INTERVAL '5 minutes'", dataConversion.Sql, StringComparison.Ordinal);
    }
}
