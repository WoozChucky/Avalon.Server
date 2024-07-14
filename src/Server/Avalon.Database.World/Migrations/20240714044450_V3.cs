using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.World.Database.Migrations
{
    /// <inheritdoc />
    public partial class V3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AllowedClasses",
                table: "ItemTemplates",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<uint>(
                name: "BuyPrice",
                table: "ItemTemplates",
                type: "int unsigned",
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<int>(
                name: "Class",
                table: "ItemTemplates",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<uint>(
                name: "DamageMax1",
                table: "ItemTemplates",
                type: "int unsigned",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "DamageMax2",
                table: "ItemTemplates",
                type: "int unsigned",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "DamageMin1",
                table: "ItemTemplates",
                type: "int unsigned",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "DamageMin2",
                table: "ItemTemplates",
                type: "int unsigned",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DamageType1",
                table: "ItemTemplates",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DamageType2",
                table: "ItemTemplates",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "DisplayId",
                table: "ItemTemplates",
                type: "int unsigned",
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<int>(
                name: "Flags",
                table: "ItemTemplates",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<ushort>(
                name: "ItemPower",
                table: "ItemTemplates",
                type: "smallint unsigned",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "MaxStackSize",
                table: "ItemTemplates",
                type: "int unsigned",
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "ItemTemplates",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "Rarity",
                table: "ItemTemplates",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<ushort>(
                name: "RequiredLevel",
                table: "ItemTemplates",
                type: "smallint unsigned",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "SellPrice",
                table: "ItemTemplates",
                type: "int unsigned",
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<int>(
                name: "Slot",
                table: "ItemTemplates",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StatType1",
                table: "ItemTemplates",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StatType10",
                table: "ItemTemplates",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StatType2",
                table: "ItemTemplates",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StatType3",
                table: "ItemTemplates",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StatType4",
                table: "ItemTemplates",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StatType5",
                table: "ItemTemplates",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StatType6",
                table: "ItemTemplates",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StatType7",
                table: "ItemTemplates",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StatType8",
                table: "ItemTemplates",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StatType9",
                table: "ItemTemplates",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "StatValue1",
                table: "ItemTemplates",
                type: "int unsigned",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "StatValue10",
                table: "ItemTemplates",
                type: "int unsigned",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "StatValue2",
                table: "ItemTemplates",
                type: "int unsigned",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "StatValue3",
                table: "ItemTemplates",
                type: "int unsigned",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "StatValue4",
                table: "ItemTemplates",
                type: "int unsigned",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "StatValue5",
                table: "ItemTemplates",
                type: "int unsigned",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "StatValue6",
                table: "ItemTemplates",
                type: "int unsigned",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "StatValue7",
                table: "ItemTemplates",
                type: "int unsigned",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "StatValue8",
                table: "ItemTemplates",
                type: "int unsigned",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "StatValue9",
                table: "ItemTemplates",
                type: "int unsigned",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SubClass",
                table: "ItemTemplates",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ItemInstances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TemplateId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    CharacterId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    Count = table.Column<uint>(type: "int unsigned", nullable: false),
                    Durability = table.Column<uint>(type: "int unsigned", nullable: false),
                    Charges = table.Column<uint>(type: "int unsigned", nullable: false),
                    Flags = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemInstances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemInstances_ItemTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "ItemTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ItemInstances_TemplateId",
                table: "ItemInstances",
                column: "TemplateId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItemInstances");

            migrationBuilder.DropColumn(
                name: "AllowedClasses",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "BuyPrice",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "Class",
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
                name: "DamageType1",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "DamageType2",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "DisplayId",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "Flags",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "ItemPower",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "MaxStackSize",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "Rarity",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "RequiredLevel",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "SellPrice",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "Slot",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "StatType1",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "StatType10",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "StatType2",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "StatType3",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "StatType4",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "StatType5",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "StatType6",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "StatType7",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "StatType8",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "StatType9",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "StatValue1",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "StatValue10",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "StatValue2",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "StatValue3",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "StatValue4",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "StatValue5",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "StatValue6",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "StatValue7",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "StatValue8",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "StatValue9",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "SubClass",
                table: "ItemTemplates");
        }
    }
}
