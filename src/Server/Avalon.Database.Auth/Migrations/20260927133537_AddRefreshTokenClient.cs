using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Auth.Migrations
{
    /// <inheritdoc />
    public partial class AddRefreshTokenClient : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "Client",
                table: "RefreshTokens",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<string>(
                name: "DeviceName",
                table: "RefreshTokens",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_AccountId_Client_Revoked",
                table: "RefreshTokens",
                columns: new[] { "AccountId", "Client", "Revoked" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_AccountId_Client_Revoked",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "Client",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "DeviceName",
                table: "RefreshTokens");
        }
    }
}
