using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <summary>
    /// #1179: a weapon's damage ranges and attack time are its base stats (ItemTemplateBaseStats), so the template drops
    /// DamageMin1, DamageMax1, DamageMin2, DamageMax2, DamageType2, AttackTimeMs and CK_ItemTemplates_AttackTimeMs, and keeps
    /// the type of its damage as DamageType. Hand-edited: EF read the change as DamageType2 renamed and DamageType1 dropped;
    /// DamageType1 is the one renamed (no seeded template set a second range), so the weapons keep their type with no
    /// update. Down restores the columns with the weapons' seeded values of Part B.
    /// </summary>
    public partial class DropTemplateWeaponColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ItemTemplates_AttackTimeMs",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "AttackTimeMs",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "DamageMax1",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "DamageMax2",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "DamageMin1",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "DamageMin2",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "DamageType2",
                table: "ItemTemplates");

            migrationBuilder.RenameColumn(
                name: "DamageType1",
                table: "ItemTemplates",
                newName: "DamageType");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "DamageType",
                table: "ItemTemplates",
                newName: "DamageType1");

            migrationBuilder.AddColumn<long>(
                name: "AttackTimeMs",
                table: "ItemTemplates",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DamageMax1",
                table: "ItemTemplates",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DamageMax2",
                table: "ItemTemplates",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DamageMin1",
                table: "ItemTemplates",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DamageMin2",
                table: "ItemTemplates",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DamageType2",
                table: "ItemTemplates",
                type: "integer",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 5m,
                columns: new[] { "AttackTimeMs", "DamageMax1", "DamageMax2", "DamageMin1", "DamageMin2", "DamageType1", "DamageType2" },
                values: new object[] { 800L, 11L, null, 7L, null, 0, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 6m,
                columns: new[] { "AttackTimeMs", "DamageMax1", "DamageMax2", "DamageMin1", "DamageMin2", "DamageType1", "DamageType2" },
                values: new object[] { 801L, 11L, null, 7L, null, 0, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 7m,
                columns: new[] { "AttackTimeMs", "DamageMax1", "DamageMax2", "DamageMin1", "DamageMin2", "DamageType1", "DamageType2" },
                values: new object[] { 792L, 11L, null, 7L, null, 0, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 8m,
                columns: new[] { "AttackTimeMs", "DamageMax1", "DamageMax2", "DamageMin1", "DamageMin2", "DamageType1", "DamageType2" },
                values: new object[] { 784L, 11L, null, 7L, null, 0, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 32m,
                columns: new[] { "AttackTimeMs", "DamageMax1", "DamageMax2", "DamageMin1", "DamageMin2", "DamageType1", "DamageType2" },
                values: new object[] { 792L, 7L, null, 4L, null, 0, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 33m,
                columns: new[] { "AttackTimeMs", "DamageMax1", "DamageMax2", "DamageMin1", "DamageMin2", "DamageType1", "DamageType2" },
                values: new object[] { 800L, 7L, null, 4L, null, 0, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 34m,
                columns: new[] { "AttackTimeMs", "DamageMax1", "DamageMax2", "DamageMin1", "DamageMin2", "DamageType1", "DamageType2" },
                values: new object[] { 801L, 7L, null, 4L, null, 0, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 35m,
                columns: new[] { "AttackTimeMs", "DamageMax1", "DamageMax2", "DamageMin1", "DamageMin2", "DamageType1", "DamageType2" },
                values: new object[] { 784L, 7L, null, 4L, null, 0, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 100m,
                columns: new[] { "AttackTimeMs", "DamageMax1", "DamageMax2", "DamageMin1", "DamageMin2", "DamageType1", "DamageType2" },
                values: new object[] { 792L, 17L, null, 11L, null, 0, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 101m,
                columns: new[] { "AttackTimeMs", "DamageMax1", "DamageMax2", "DamageMin1", "DamageMin2", "DamageType1", "DamageType2" },
                values: new object[] { 784L, 17L, null, 11L, null, 0, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 102m,
                columns: new[] { "AttackTimeMs", "DamageMax1", "DamageMax2", "DamageMin1", "DamageMin2", "DamageType1", "DamageType2" },
                values: new object[] { 800L, 17L, null, 11L, null, 0, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 103m,
                columns: new[] { "AttackTimeMs", "DamageMax1", "DamageMax2", "DamageMin1", "DamageMin2", "DamageType1", "DamageType2" },
                values: new object[] { 801L, 17L, null, 11L, null, 0, null });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ItemTemplates_AttackTimeMs",
                table: "ItemTemplates",
                sql: "\"AttackTimeMs\" IS NULL OR \"AttackTimeMs\" > 0");
        }
    }
}
