using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Crashes.Migrations
{
    /// <inheritdoc />
    public partial class SanitizedCrashTextQuality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_crash_reports_quality",
                table: "crash_reports");

            migrationBuilder.AddCheckConstraint(
                name: "ck_crash_reports_quality",
                table: "crash_reports",
                sql: "quality_flags ~ '^(none|(missingSystemSymbols|scanFrames|truncated|metadataDisagreement|versionComparisonUnavailable|sanitizedText)(, (missingSystemSymbols|scanFrames|truncated|metadataDisagreement|versionComparisonUnavailable|sanitizedText))*)$'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_crash_reports_quality",
                table: "crash_reports");

            migrationBuilder.AddCheckConstraint(
                name: "ck_crash_reports_quality",
                table: "crash_reports",
                sql: "quality_flags ~ '^(none|(missingSystemSymbols|scanFrames|truncated|metadataDisagreement|versionComparisonUnavailable)(, (missingSystemSymbols|scanFrames|truncated|metadataDisagreement|versionComparisonUnavailable))*)$'");
        }
    }
}
