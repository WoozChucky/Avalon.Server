using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Auth.Migrations
{
    /// <inheritdoc />
    public partial class AccountEmailVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EmailVerifiedAt",
                table: "Accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AccountEmailVerifications",
                columns: table => new
                {
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    CredentialsVersion = table.Column<int>(type: "integer", nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InvalidatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountEmailVerifications", x => x.AccountId);
                    table.CheckConstraint("CK_AccountEmailVerifications_Interval", "\"ExpiresAt\" > \"IssuedAt\" AND length(\"TokenHash\") = 64");
                    table.ForeignKey(
                        name: "FK_AccountEmailVerifications_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 1L,
                column: "EmailVerifiedAt",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountEmailVerifications");

            migrationBuilder.DropColumn(
                name: "EmailVerifiedAt",
                table: "Accounts");
        }
    }
}
