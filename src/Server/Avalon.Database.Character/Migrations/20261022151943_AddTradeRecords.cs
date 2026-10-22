using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Character.Migrations
{
    /// <inheritdoc />
    public partial class AddTradeRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TradeRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstCharacterId = table.Column<long>(type: "bigint", nullable: false),
                    SecondCharacterId = table.Column<long>(type: "bigint", nullable: false),
                    FirstGold = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    SecondGold = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TradeRecordItems",
                columns: table => new
                {
                    TradeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    GiverCharacterId = table.Column<long>(type: "bigint", nullable: false),
                    TemplateId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Count = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeRecordItems", x => new { x.TradeId, x.ItemInstanceId });
                    table.ForeignKey(
                        name: "FK_TradeRecordItems_TradeRecords_TradeId",
                        column: x => x.TradeId,
                        principalTable: "TradeRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TradeRecordItems_ItemInstanceId",
                table: "TradeRecordItems",
                column: "ItemInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeRecords_FirstCharacterId",
                table: "TradeRecords",
                column: "FirstCharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeRecords_SecondCharacterId",
                table: "TradeRecords",
                column: "SecondCharacterId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TradeRecordItems");

            migrationBuilder.DropTable(
                name: "TradeRecords");
        }
    }
}
