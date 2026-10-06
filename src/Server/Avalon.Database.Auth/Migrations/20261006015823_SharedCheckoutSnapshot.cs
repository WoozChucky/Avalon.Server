using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Auth.Migrations
{
    /// <inheritdoc />
    public partial class SharedCheckoutSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentAttempts_OrderId",
                table: "PaymentAttempts");

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethods",
                table: "PaymentAttempts",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProviderCatalogProductId",
                table: "PaymentAttempts",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "RequestedExpiresAt",
                table: "PaymentAttempts",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "Sequence",
                table: "PaymentAttempts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("""
                WITH ordered AS (
                    SELECT "Id", row_number() OVER (PARTITION BY "OrderId" ORDER BY "CreatedAt", "Id") AS sequence
                    FROM "PaymentAttempts"
                )
                UPDATE "PaymentAttempts" AS attempt SET "Sequence" = ordered.sequence
                FROM ordered WHERE attempt."Id" = ordered."Id";
                UPDATE "PaymentAttempts" SET "State" = 'NeedsReview', "Version" = "Version" + 1
                WHERE "State" IN ('Reserved', 'ProviderUnknown') AND "Version" < 9223372036854775807;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAttempts_OrderId_Sequence",
                table: "PaymentAttempts",
                columns: new[] { "OrderId", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentAttempts_OrderId_Sequence",
                table: "PaymentAttempts");

            migrationBuilder.DropColumn(
                name: "PaymentMethods",
                table: "PaymentAttempts");

            migrationBuilder.DropColumn(
                name: "ProviderCatalogProductId",
                table: "PaymentAttempts");

            migrationBuilder.DropColumn(
                name: "RequestedExpiresAt",
                table: "PaymentAttempts");

            migrationBuilder.DropColumn(
                name: "Sequence",
                table: "PaymentAttempts");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAttempts_OrderId",
                table: "PaymentAttempts",
                column: "OrderId");
        }
    }
}
