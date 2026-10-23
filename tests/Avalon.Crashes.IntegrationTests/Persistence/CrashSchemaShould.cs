using Avalon.Database.Crashes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Avalon.Crashes.IntegrationTests.Persistence;

public sealed class CrashSchemaShould : IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();
    public Task InitializeAsync() => _postgres.InitializeAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync();

    [IntegrationFact]
    public async Task Should_keep_schema_in_sync()
    {
        await using CrashesDbContext db = _postgres.Factory.CreateDbContext();
        Assert.False(db.Database.HasPendingModelChanges());
        string[] before = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Single(before);
        await new CrashMigrator(_postgres.Factory).MigrateAsync(default);
        Assert.Equal(before, await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(1, await db.DiscoveryCursors.CountAsync());
    }
}
