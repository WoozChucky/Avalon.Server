using Avalon.Database.Crashes.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Avalon.Database.Crashes.Configuration;

public sealed class CrashDiscoveryVersionConfiguration : IEntityTypeConfiguration<CrashDiscoveryVersion>
{
    public void Configure(EntityTypeBuilder<CrashDiscoveryVersion> builder)
    {
        builder.ToTable("crash_discovery_versions", table =>
        {
            table.HasCheckConstraint("ck_crash_discovery_versions_version", "version ~ '^[A-Za-z0-9.+-]{1,64}$' AND version NOT IN ('.','..')");
            table.HasCheckConstraint("ck_crash_discovery_versions_phase", "phase BETWEEN 0 AND 3");
        });
        builder.HasKey(x => x.Version);
        builder.Property(x => x.Version).HasMaxLength(64);
        builder.Property(x => x.TodayToken).HasMaxLength(4096);
        builder.Property(x => x.YesterdayToken).HasMaxLength(4096);
        builder.Property(x => x.DatePrefixToken).HasMaxLength(4096);
        builder.Property(x => x.HistoricalToken).HasMaxLength(4096);
        builder.HasIndex(x => new { x.LastVisitedAt, x.Version });
        builder.HasIndex(x => x.LastSeenAt);
    }
}
