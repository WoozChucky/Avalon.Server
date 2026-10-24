using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Crashes.Migrations
{
    /// <inheritdoc />
    public partial class CrashRetryCampaignAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "retry_campaign_attempt_count",
                table: "crash_reports",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Existing rows belong to their original campaign; preserve its backoff position.
            migrationBuilder.Sql("UPDATE crash_reports SET retry_campaign_attempt_count = attempt_count");

            migrationBuilder.AddCheckConstraint(
                name: "ck_crash_reports_campaign_attempts",
                table: "crash_reports",
                sql: "retry_campaign_attempt_count >= 0 AND retry_campaign_attempt_count <= attempt_count");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_crash_reports_campaign_attempts",
                table: "crash_reports");

            migrationBuilder.DropColumn(
                name: "retry_campaign_attempt_count",
                table: "crash_reports");
        }
    }
}
