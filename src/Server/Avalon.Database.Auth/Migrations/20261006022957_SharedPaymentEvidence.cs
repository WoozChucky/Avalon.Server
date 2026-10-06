using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Auth.Migrations
{
    /// <inheritdoc />
    public partial class SharedPaymentEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "RequestedBy",
                table: "PaymentRefunds",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<string>(
                name: "PaymentReference",
                table: "PaymentEvents",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResourceKind",
                table: "PaymentEvents",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastReconciledAt",
                table: "PaymentAttempts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "PaymentEvents" SET "State" = 'NeedsReview', "FailureCode" = 'INCOMPLETE_EVENT_BINDING',
                    "LeaseId" = NULL, "LeaseUntil" = NULL, "Version" = "Version" + 1
                WHERE "ResourceKind" = '' AND "State" IN ('Pending', 'Processing') AND "Version" < 9223372036854775807;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaymentReference",
                table: "PaymentEvents");

            migrationBuilder.DropColumn(
                name: "ResourceKind",
                table: "PaymentEvents");

            migrationBuilder.DropColumn(
                name: "LastReconciledAt",
                table: "PaymentAttempts");

            migrationBuilder.AlterColumn<long>(
                name: "RequestedBy",
                table: "PaymentRefunds",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);
        }
    }
}
