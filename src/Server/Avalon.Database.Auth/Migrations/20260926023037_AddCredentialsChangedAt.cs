using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Auth.Migrations
{
    /// <inheritdoc />
    public partial class AddCredentialsChangedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // #495: when the account's credentials last changed. NULL means never, so no row is
            // backfilled: every credential issued before this migration stays as valid as it was.
            migrationBuilder.AddColumn<DateTime>(
                name: "CredentialsChangedAt",
                table: "Accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 1L,
                column: "CredentialsChangedAt",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CredentialsChangedAt",
                table: "Accounts");
        }
    }
}
