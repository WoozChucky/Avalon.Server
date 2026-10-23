using Avalon.Database.Crashes.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Avalon.Database.Crashes.Configuration;

public sealed class CrashDiscoveryDateConfiguration : IEntityTypeConfiguration<CrashDiscoveryDate>
{
    public void Configure(EntityTypeBuilder<CrashDiscoveryDate> builder)
    {
        builder.ToTable("crash_discovery_dates");
        builder.HasKey(x => new { x.Version, x.Date });
        builder.Property(x => x.Version).HasMaxLength(64);
        builder.HasOne<CrashDiscoveryVersion>().WithMany().HasForeignKey(x => x.Version).OnDelete(DeleteBehavior.Cascade);
    }
}
