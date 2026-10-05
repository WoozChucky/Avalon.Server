using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Auth.Migrations
{
    /// <inheritdoc />
    public partial class SharedStoreProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "SteamSubject",
                table: "AccountConsolidations",
                newName: "ProviderSubject");

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "StoreAccountCreations",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "steam");

            migrationBuilder.AlterColumn<string>(
                name: "Provider",
                table: "LicenseObservations",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AlterColumn<string>(
                name: "Provider",
                table: "ExternalIdentities",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "AccountConsolidations",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "steam");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Provider",
                table: "StoreAccountCreations");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "AccountConsolidations");

            migrationBuilder.RenameColumn(
                name: "ProviderSubject",
                table: "AccountConsolidations",
                newName: "SteamSubject");

            migrationBuilder.AlterColumn<string>(
                name: "Provider",
                table: "LicenseObservations",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "Provider",
                table: "ExternalIdentities",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);
        }
    }
}
