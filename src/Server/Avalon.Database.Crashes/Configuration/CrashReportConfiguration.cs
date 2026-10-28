using Avalon.Crashes;
using Avalon.Database.Crashes.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Avalon.Database.Crashes.Configuration;

public sealed class CrashReportConfiguration : IEntityTypeConfiguration<CrashReport>
{
    public void Configure(EntityTypeBuilder<CrashReport> builder)
    {
        builder.ToTable("crash_reports", table =>
        {
            table.HasCheckConstraint("ck_crash_reports_status", "status IN ('pending','processing','retry_wait','processed','rejected','failed')");
            table.HasCheckConstraint("ck_crash_reports_key_bytes", "octet_length(object_key) BETWEEN 1 AND 1024");
            table.HasCheckConstraint("ck_crash_reports_source_size", "source_size >= 0");
            table.HasCheckConstraint("ck_crash_reports_expiry", "expires_at <= received_at + INTERVAL '90 days' AND expires_at > received_at");
            table.HasCheckConstraint("ck_crash_reports_attempts", "attempt_count >= 0 AND consecutive_tool_failures >= 0");
            table.HasCheckConstraint("ck_crash_reports_campaign_attempts", "retry_campaign_attempt_count >= 0 AND retry_campaign_attempt_count <= attempt_count");
            table.HasCheckConstraint("ck_crash_reports_lease", "(status = 'processing') = (lease_owner IS NOT NULL AND lease_until IS NOT NULL)");
            table.HasCheckConstraint("ck_crash_reports_processed", "(status = 'processed') = (group_id IS NOT NULL AND stack IS NOT NULL AND processed_at IS NOT NULL AND signature_version IS NOT NULL)");
            table.HasCheckConstraint("ck_crash_reports_no_failed_input", "status NOT IN ('rejected','failed') OR (meta IS NULL AND stack IS NULL AND log_tail IS NULL AND version IS NULL AND git_hash IS NULL AND client_crashed_at IS NULL AND gpu_name IS NULL AND vulkan_driver_version IS NULL)");
            table.HasCheckConstraint("ck_crash_reports_reason", "reason_code IS NULL OR reason_code IN ('unsafe_archive','archive_too_large','metadata_too_large','log_too_large','invalid_metadata','unsupported_schema','invalid_dump','unsupported_architecture','runtime_identity_mismatch','object_changed','storage_unavailable','missing_runtime_symbols','tool_timeout','tool_failed','resource_limit','invalid_analysis','ungroupable_analysis','configuration_invalid','access_denied','object_missing','invalid_object_key')");
            table.HasCheckConstraint("ck_crash_reports_driver_source", "gpu_driver_source IS NULL OR gpu_driver_source IN ('faulting_module','loaded_module','unknown')");
            table.HasCheckConstraint("ck_crash_reports_quality", "quality_flags ~ '^(none|(missingSystemSymbols|scanFrames|truncated|metadataDisagreement|versionComparisonUnavailable|sanitizedText)(, (missingSystemSymbols|scanFrames|truncated|metadataDisagreement|versionComparisonUnavailable|sanitizedText))*)$'");
            table.HasCheckConstraint("ck_crash_reports_exception_code", "exception_code IS NULL OR exception_code BETWEEN 0 AND 4294967295");
            table.HasCheckConstraint("ck_crash_reports_log_size", "log_tail IS NULL OR octet_length(log_tail) <= 65536");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RetryCampaignAttemptCount).HasDefaultValue(0L);
        builder.Property(x => x.Id).HasConversion(x => x.Value, x => new CrashReportId(x)).ValueGeneratedNever();
        builder.Property(x => x.GroupId).HasConversion(x => x == null ? (Guid?)null : x.Value, x => x == null ? null : new CrashGroupId(x.Value));
        builder.HasOne<CrashGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.Status).HasConversion(x => CrashEnumStorage.Write(x), x => CrashEnumStorage.Read<CrashReportStatus>(x)).HasMaxLength(16);
        builder.Property(x => x.ReasonCode).HasConversion(x => x == null ? null : CrashEnumStorage.Write(x.Value), x => x == null ? null : CrashEnumStorage.Read<CrashFailureCode>(x)).HasMaxLength(64);
        builder.Property(x => x.QualityFlags).HasConversion(x => CrashEnumStorage.Write(x), x => CrashEnumStorage.Read<CrashQualityFlags>(x)).HasMaxLength(128);
        builder.Property(x => x.Meta).HasColumnType("jsonb");
        builder.Property(x => x.Stack).HasColumnType("jsonb");
        builder.Property(x => x.ObjectKey).HasMaxLength(1024);
        builder.Property(x => x.SourceETag).HasMaxLength(1024);
        builder.Property(x => x.Version).HasMaxLength(64);
        builder.Property(x => x.GitHash).HasMaxLength(128);
        builder.Property(x => x.ReasonDetail).HasMaxLength(512);
        builder.Property(x => x.ExceptionAddress).HasMaxLength(18);
        builder.Property(x => x.CrashingModule).HasMaxLength(128);
        builder.Property(x => x.OsVersion).HasMaxLength(512);
        builder.Property(x => x.OsBuild).HasMaxLength(512);
        builder.Property(x => x.Cpu).HasMaxLength(512);
        builder.Property(x => x.GpuDriverModule).HasMaxLength(128);
        builder.Property(x => x.GpuDriverVersion).HasMaxLength(128);
        builder.Property(x => x.GpuDriverSource).HasMaxLength(32);
        builder.Property(x => x.GpuName).HasMaxLength(128);
        builder.Property(x => x.VulkanDriverVersion).HasMaxLength(32);
        builder.Property(x => x.ProcessorVersion).HasMaxLength(128);
        builder.Property(x => x.ConverterVersion).HasMaxLength(128);
        builder.Property(x => x.CacheVersion).HasMaxLength(128);
        builder.HasIndex(x => x.ObjectKey).IsUnique();
        builder.HasIndex(x => x.ExpiresAt);
        builder.HasIndex(x => new { x.Status, x.NextAttemptAt });
        builder.HasIndex(x => x.LeaseUntil).HasFilter("status = 'processing'");
        builder.HasIndex(x => new { x.GroupId, x.ReceivedAt, x.Id }).IsDescending(false, true, false);
        builder.HasIndex(x => new { x.Version, x.ReceivedAt, x.GroupId });
    }
}
