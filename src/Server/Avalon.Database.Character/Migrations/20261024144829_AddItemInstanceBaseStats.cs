using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Character.Migrations
{
    /// <inheritdoc />
    public partial class AddItemInstanceBaseStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ItemInstanceBaseStats",
                columns: table => new
                {
                    ItemInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Stat = table.Column<byte>(type: "smallint", nullable: false),
                    Value = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemInstanceBaseStats", x => new { x.ItemInstanceId, x.Stat });
                    table.CheckConstraint("CK_ItemInstanceBaseStats_Stat", "\"Stat\" BETWEEN 0 AND 6");
                    table.CheckConstraint("CK_ItemInstanceBaseStats_Value", "\"Value\" >= 0");
                    table.ForeignKey(
                        name: "FK_ItemInstanceBaseStats_ItemInstances_ItemInstanceId",
                        column: x => x.ItemInstanceId,
                        principalTable: "ItemInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TradeRecordItemBaseStats",
                columns: table => new
                {
                    TradeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Stat = table.Column<byte>(type: "smallint", nullable: false),
                    Value = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeRecordItemBaseStats", x => new { x.TradeId, x.ItemInstanceId, x.Stat });
                    table.CheckConstraint("CK_TradeRecordItemBaseStats_Stat", "\"Stat\" BETWEEN 0 AND 6");
                    table.CheckConstraint("CK_TradeRecordItemBaseStats_Value", "\"Value\" >= 0");
                    table.ForeignKey(
                        name: "FK_TradeRecordItemBaseStats_TradeRecordItems_TradeId_ItemInsta~",
                        columns: x => new { x.TradeId, x.ItemInstanceId },
                        principalTable: "TradeRecordItems",
                        principalColumns: new[] { "TradeId", "ItemInstanceId" },
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItemInstanceBaseStats");

            migrationBuilder.DropTable(
                name: "TradeRecordItemBaseStats");
        }
    }
}
