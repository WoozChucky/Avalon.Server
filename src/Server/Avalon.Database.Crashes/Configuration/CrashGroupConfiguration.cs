using Avalon.Crashes;
using Avalon.Database.Crashes.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Avalon.Database.Crashes.Configuration;

public sealed class CrashGroupConfiguration : IEntityTypeConfiguration<CrashGroup>
{
    public void Configure(EntityTypeBuilder<CrashGroup> builder)
    {
        builder.ToTable("crash_groups", table =>
        {
            table.HasCheckConstraint("ck_crash_groups_status", "status IN ('new','investigating','fixed','ignored')");
            table.HasCheckConstraint("ck_crash_groups_counts", "report_count >= 0 AND regression_count >= 0 AND triage_revision >= 0");
            table.HasCheckConstraint("ck_crash_groups_extrema", "first_seen <= last_seen");
            table.HasCheckConstraint("ck_crash_groups_hash", "signature_hash ~ '^[0-9a-f]{64}$'");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(x => x.Value, x => new CrashGroupId(x)).ValueGeneratedNever();
        builder.Property(x => x.Status).HasConversion(x => CrashEnumStorage.Write(x), x => CrashEnumStorage.Read<CrashGroupStatus>(x)).HasMaxLength(16).HasDefaultValue(CrashGroupStatus.New);
        builder.Property(x => x.SignatureHash).HasMaxLength(64);
        builder.Property(x => x.Signature).HasMaxLength(4096);
        builder.Property(x => x.FirstVersion).HasMaxLength(64);
        builder.Property(x => x.LastVersion).HasMaxLength(64);
        builder.Property(x => x.FixedInVersion).HasMaxLength(64);
        builder.Property(x => x.IssueUrl).HasMaxLength(2048);
        builder.Property(x => x.Notes).HasMaxLength(4096);
        builder.HasIndex(x => new { x.SignatureVersion, x.SignatureHash }).IsUnique();
        builder.HasIndex(x => new { x.Status, x.LastSeen }).IsDescending(false, true);
    }
}
