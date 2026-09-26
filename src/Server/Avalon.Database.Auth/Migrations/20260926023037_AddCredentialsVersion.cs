using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Auth.Migrations
{
    /// <inheritdoc />
    public partial class AddCredentialsVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // #495: a counter every password change, MFA reset and admin MFA removal raises by one.
            // Every existing account starts at 0, and so does every existing refresh token, so the
            // sessions open at migration time stay as valid as they were.
            migrationBuilder.AddColumn<int>(
                name: "CredentialsVersion",
                table: "Accounts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // The account's version when the family's login proved the credentials.
            migrationBuilder.AddColumn<int>(
                name: "CredentialsVersion",
                table: "RefreshTokens",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Who rotated a token in (#495 review): the grace for its parent's replay is only for them.
            // A family's first token, and every token from before this migration, has neither.
            migrationBuilder.AddColumn<string>(
                name: "RotatedBySource",
                table: "RefreshTokens",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RotatedByAgentHash",
                table: "RefreshTokens",
                type: "bytea",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 1L,
                column: "CredentialsVersion",
                value: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RotatedByAgentHash",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "RotatedBySource",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "CredentialsVersion",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "CredentialsVersion",
                table: "Accounts");
        }
    }
}
