using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Crashes;

public sealed class CrashMigrator(IDbContextFactory<CrashesDbContext> factory)
{
    public async Task MigrateAsync(CancellationToken ct)
    {
        await using CrashesDbContext db = await factory.CreateDbContextAsync(ct);
        await db.Database.MigrateAsync(ct);
    }
}
