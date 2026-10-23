using Avalon.Database.Crashes.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Avalon.Database.Crashes.Configuration;

public sealed class CrashDiscoveryCursorConfiguration : IEntityTypeConfiguration<CrashDiscoveryCursor>
{
    public void Configure(EntityTypeBuilder<CrashDiscoveryCursor> builder)
    {
        builder.ToTable("crash_discovery_cursor", table => table.HasCheckConstraint("ck_crash_discovery_cursor_singleton", "id = 1"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ContinuationToken).HasMaxLength(4096);
        builder.Property(x => x.BackfillContinuationToken).HasMaxLength(4096);
        builder.HasData(new CrashDiscoveryCursor { Id = 1 });
    }
}
