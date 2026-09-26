using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class AddAbilityShapesAndBodyRadius : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 0.5, not EF's 0: every existing row must satisfy CK_CreatureTemplates_BodyRadius_Positive,
            // added below, the moment it exists (#164).
            migrationBuilder.AddColumn<float>(
                name: "BodyRadius",
                table: "CreatureTemplates",
                type: "real",
                nullable: false,
                defaultValue: 0.5f);

            migrationBuilder.AddColumn<byte>(
                name: "Affects",
                table: "AbilityTemplates",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<byte>(
                name: "AimMode",
                table: "AbilityTemplates",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<byte>(
                name: "Anchor",
                table: "AbilityTemplates",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<float>(
                name: "ArcDegrees",
                table: "AbilityTemplates",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<bool>(
                name: "Pierce",
                table: "AbilityTemplates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<float>(
                name: "ProjectileSpeed",
                table: "AbilityTemplates",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "Radius",
                table: "AbilityTemplates",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "Reach",
                table: "AbilityTemplates",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<byte>(
                name: "Shape",
                table: "AbilityTemplates",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 1L,
                columns: new[] { "Affects", "AimMode", "Anchor", "ArcDegrees", "Pierce", "ProjectileSpeed", "Radius", "Reach", "Shape" },
                values: new object[] { (byte)0, (byte)0, (byte)0, 0f, false, 0f, 0f, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 2L,
                columns: new[] { "Affects", "AimMode", "Anchor", "ArcDegrees", "Pierce", "ProjectileSpeed", "Radius", "Reach", "Shape" },
                values: new object[] { (byte)0, (byte)0, (byte)0, 0f, false, 0f, 0f, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 100L,
                columns: new[] { "Affects", "AimMode", "Anchor", "ArcDegrees", "Pierce", "ProjectileSpeed", "Radius", "Reach", "Shape" },
                values: new object[] { (byte)0, (byte)0, (byte)0, 0f, false, 0f, 0f, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 101L,
                columns: new[] { "Affects", "AimMode", "Anchor", "ArcDegrees", "Pierce", "ProjectileSpeed", "Radius", "Reach", "Shape" },
                values: new object[] { (byte)0, (byte)0, (byte)0, 0f, false, 0f, 0f, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 102L,
                columns: new[] { "Affects", "AimMode", "Anchor", "ArcDegrees", "Pierce", "ProjectileSpeed", "Radius", "Reach", "Shape" },
                values: new object[] { (byte)0, (byte)0, (byte)0, 0f, false, 0f, 0f, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 103L,
                columns: new[] { "Affects", "AimMode", "Anchor", "ArcDegrees", "Pierce", "ProjectileSpeed", "Radius", "Reach", "Shape" },
                values: new object[] { (byte)0, (byte)0, (byte)0, 0f, false, 0f, 0f, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 1m,
                column: "BodyRadius",
                value: 0.5f);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 2m,
                column: "BodyRadius",
                value: 0.5f);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 3m,
                column: "BodyRadius",
                value: 0.5f);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 4m,
                column: "BodyRadius",
                value: 0.5f);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 5m,
                column: "BodyRadius",
                value: 0.5f);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 6m,
                column: "BodyRadius",
                value: 0.5f);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 7m,
                column: "BodyRadius",
                value: 0.5f);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 8m,
                column: "BodyRadius",
                value: 0.5f);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 9m,
                column: "BodyRadius",
                value: 0.5f);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 10m,
                column: "BodyRadius",
                value: 0.5f);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 11m,
                column: "BodyRadius",
                value: 0.5f);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 12m,
                column: "BodyRadius",
                value: 0.5f);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 13m,
                column: "BodyRadius",
                value: 0.5f);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 14m,
                column: "BodyRadius",
                value: 0.5f);

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreatureTemplates_BodyRadius_Positive",
                table: "CreatureTemplates",
                sql: "\"BodyRadius\" > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CreatureTemplates_BodyRadius_Positive",
                table: "CreatureTemplates");

            migrationBuilder.DropColumn(
                name: "BodyRadius",
                table: "CreatureTemplates");

            migrationBuilder.DropColumn(
                name: "Affects",
                table: "AbilityTemplates");

            migrationBuilder.DropColumn(
                name: "AimMode",
                table: "AbilityTemplates");

            migrationBuilder.DropColumn(
                name: "Anchor",
                table: "AbilityTemplates");

            migrationBuilder.DropColumn(
                name: "ArcDegrees",
                table: "AbilityTemplates");

            migrationBuilder.DropColumn(
                name: "Pierce",
                table: "AbilityTemplates");

            migrationBuilder.DropColumn(
                name: "ProjectileSpeed",
                table: "AbilityTemplates");

            migrationBuilder.DropColumn(
                name: "Radius",
                table: "AbilityTemplates");

            migrationBuilder.DropColumn(
                name: "Reach",
                table: "AbilityTemplates");

            migrationBuilder.DropColumn(
                name: "Shape",
                table: "AbilityTemplates");
        }
    }
}
