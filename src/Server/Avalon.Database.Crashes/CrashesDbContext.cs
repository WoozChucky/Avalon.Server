using Avalon.Database.Crashes.Configuration;
using Avalon.Database.Crashes.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Avalon.Database.Crashes;

public sealed class CrashesDbContext(DbContextOptions<CrashesDbContext> options) : DbContext(options)
{
    public DbSet<CrashReport> Reports => Set<CrashReport>();
    public DbSet<CrashGroup> Groups => Set<CrashGroup>();
    public DbSet<CrashDiscoveryCursor> DiscoveryCursors => Set<CrashDiscoveryCursor>();
    public DbSet<CrashDiscoveryVersion> DiscoveryVersions => Set<CrashDiscoveryVersion>();
    public DbSet<CrashDiscoveryDate> DiscoveryDates => Set<CrashDiscoveryDate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new CrashReportConfiguration());
        modelBuilder.ApplyConfiguration(new CrashGroupConfiguration());
        modelBuilder.ApplyConfiguration(new CrashDiscoveryCursorConfiguration());
        modelBuilder.ApplyConfiguration(new CrashDiscoveryVersionConfiguration());
        modelBuilder.ApplyConfiguration(new CrashDiscoveryDateConfiguration());
        foreach (IMutableEntityType entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (IMutableProperty property in entity.GetProperties())
                property.SetColumnName(property.Name == nameof(CrashReport.SourceETag) ? "source_etag" : ToSnakeCase(property.Name));
        }
    }

    private static string ToSnakeCase(string name) => string.Concat(name.Select((c, i) => char.IsUpper(c) && i > 0 && (!char.IsUpper(name[i - 1]) || i + 1 < name.Length && char.IsLower(name[i + 1])) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
}
