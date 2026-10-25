using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Avalon.Database.Crashes;

public sealed class CrashesDbContextFactory : IDbContextFactory<CrashesDbContext>, IDesignTimeDbContextFactory<CrashesDbContext>
{
    private readonly DbContextOptions<CrashesDbContext>? _options;

    public CrashesDbContextFactory()
    {
    }

    public CrashesDbContextFactory(CrashDatabaseOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ConnectionString);
        _options = new DbContextOptionsBuilder<CrashesDbContext>().UseNpgsql(options.ConnectionString).Options;
    }

    public CrashesDbContext CreateDbContext() => new(_options ?? throw new InvalidOperationException("Runtime crash database options are required."));

    public CrashesDbContext CreateDbContext(string[] args)
    {
        string connectionString = Environment.GetEnvironmentVariable("Database__Crashes__ConnectionString")
            ?? throw new InvalidOperationException("Set Database__Crashes__ConnectionString for crash schema design time.");
        return new CrashesDbContextFactory(new CrashDatabaseOptions { ConnectionString = connectionString }).CreateDbContext();
    }
}
