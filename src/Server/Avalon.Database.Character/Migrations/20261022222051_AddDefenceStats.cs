using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Character.Migrations
{
    /// <inheritdoc />
    public partial class AddDefenceStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "BlightResistance",
                table: "CharacterStats",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "ColdResistance",
                table: "CharacterStats",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<long>(
                name: "Evasion",
                table: "CharacterStats",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<float>(
                name: "FireResistance",
                table: "CharacterStats",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "LightningResistance",
                table: "CharacterStats",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<long>(
                name: "MaxWard",
                table: "CharacterStats",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "DamageType",
                table: "CharacterAuras",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BlightResistance",
                table: "CharacterStats");

            migrationBuilder.DropColumn(
                name: "ColdResistance",
                table: "CharacterStats");

            migrationBuilder.DropColumn(
                name: "Evasion",
                table: "CharacterStats");

            migrationBuilder.DropColumn(
                name: "FireResistance",
                table: "CharacterStats");

            migrationBuilder.DropColumn(
                name: "LightningResistance",
                table: "CharacterStats");

            migrationBuilder.DropColumn(
                name: "MaxWard",
                table: "CharacterStats");

            migrationBuilder.DropColumn(
                name: "DamageType",
                table: "CharacterAuras");
        }
    }
}
