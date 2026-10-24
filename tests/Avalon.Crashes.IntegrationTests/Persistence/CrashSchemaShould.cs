using Avalon.Database.Crashes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Avalon.Crashes.IntegrationTests.Persistence;

[Trait("Category", "CrashesPostgres")]
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
        Assert.Equal(db.Database.GetMigrations(), before);
        await new CrashMigrator(_postgres.Factory).MigrateAsync(default);
        Assert.Equal(before, await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(1, await db.DiscoveryCursors.CountAsync());
    }

    [IntegrationFact]
    public async Task Should_backfill_campaign_attempts_from_existing_lifetime_attempts()
    {
        await using CrashesDbContext db = _postgres.Factory.CreateDbContext();
        IMigrator migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261023224300_ResumableCrashDiscovery");
        DateTimeOffset now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
        var source = new Avalon.Crashes.StoredCrashObject("1.0.0/2026-10-24/11111111111111111111111111111111.zip", "etag", 1, now);
        await _postgres.Queue.DiscoverAsync(source, now, default);
        await db.Database.ExecuteSqlRawAsync("UPDATE crash_reports SET attempt_count = 7");
        await migrator.MigrateAsync();
        Assert.Equal(7L, (await db.Reports.SingleAsync()).RetryCampaignAttemptCount);
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
