using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Auth.Migrations
{
    /// <inheritdoc />
    public partial class LockoutExpiryAndTotpReplay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "LastAcceptedTotpStep",
                table: "MfaSetups",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockedUntil",
                table: "Accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 1L,
                column: "LockedUntil",
                value: null);

            // Every lock set before this migration came from failed logins and had no end. Give each
            // one the default lockout from now, so no account stays locked forever. A NULL
            // LockedUntil still means "no end" in the code, for a lock set by hand from here on.
            migrationBuilder.Sql(
                "UPDATE \"Accounts\" SET \"LockedUntil\" = now() + interval '15 minutes' " +
                "WHERE \"Locked\" AND \"LockedUntil\" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastAcceptedTotpStep",
                table: "MfaSetups");

            migrationBuilder.DropColumn(
                name: "LockedUntil",
                table: "Accounts");
        }
    }
}
