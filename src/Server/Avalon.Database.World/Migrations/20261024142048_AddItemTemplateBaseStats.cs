using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class AddItemTemplateBaseStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ItemTemplateBaseStats",
                columns: table => new
                {
                    TemplateId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Stat = table.Column<byte>(type: "smallint", nullable: false),
                    Min = table.Column<int>(type: "integer", nullable: false),
                    Max = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemTemplateBaseStats", x => new { x.TemplateId, x.Stat });
                    table.CheckConstraint("CK_ItemTemplateBaseStats_Range", "\"Min\" >= 0 AND \"Min\" <= \"Max\"");
                    table.CheckConstraint("CK_ItemTemplateBaseStats_Stat", "\"Stat\" BETWEEN 0 AND 6");
                    table.ForeignKey(
                        name: "FK_ItemTemplateBaseStats_ItemTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "ItemTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "ItemTemplateBaseStats",
                columns: new[] { "Stat", "TemplateId", "Max", "Min" },
                values: new object[,]
                {
                    { (byte)4, 5m, 10, 6 },
                    { (byte)5, 5m, 18, 12 },
                    { (byte)6, 5m, 1050, 950 },
                    { (byte)4, 6m, 8, 6 },
                    { (byte)5, 6m, 13, 9 },
                    { (byte)6, 6m, 840, 760 },
                    { (byte)4, 7m, 7, 5 },
                    { (byte)5, 7m, 12, 8 },
                    { (byte)6, 7m, 735, 665 },
                    { (byte)4, 8m, 8, 6 },
                    { (byte)5, 8m, 17, 11 },
                    { (byte)6, 8m, 945, 855 },
                    { (byte)0, 12m, 13, 9 },
                    { (byte)0, 13m, 22, 14 },
                    { (byte)0, 14m, 17, 11 },
                    { (byte)0, 15m, 8, 6 },
                    { (byte)0, 16m, 8, 6 },
                    { (byte)2, 17m, 19, 13 },
                    { (byte)2, 18m, 31, 21 },
                    { (byte)2, 19m, 25, 17 },
                    { (byte)2, 20m, 12, 8 },
                    { (byte)2, 21m, 12, 8 },
                    { (byte)1, 22m, 65, 43 },
                    { (byte)1, 23m, 108, 72 },
                    { (byte)1, 24m, 86, 58 },
                    { (byte)1, 25m, 43, 29 },
                    { (byte)1, 26m, 43, 29 },
                    { (byte)4, 32m, 4, 2 },
                    { (byte)5, 32m, 8, 6 },
                    { (byte)6, 32m, 735, 665 },
                    { (byte)4, 33m, 6, 4 },
                    { (byte)5, 33m, 11, 7 },
                    { (byte)6, 33m, 1050, 950 },
                    { (byte)4, 34m, 5, 3 },
                    { (byte)5, 34m, 8, 6 },
                    { (byte)6, 34m, 840, 760 },
                    { (byte)4, 35m, 5, 3 },
                    { (byte)5, 35m, 11, 7 },
                    { (byte)6, 35m, 945, 855 },
                    { (byte)0, 36m, 6, 4 },
                    { (byte)0, 37m, 10, 6 },
                    { (byte)0, 38m, 7, 5 },
                    { (byte)0, 39m, 4, 2 },
                    { (byte)0, 40m, 4, 2 },
                    { (byte)2, 41m, 8, 6 },
                    { (byte)2, 42m, 14, 10 },
                    { (byte)2, 43m, 12, 8 },
                    { (byte)2, 44m, 6, 4 },
                    { (byte)2, 45m, 6, 4 },
                    { (byte)1, 46m, 29, 19 },
                    { (byte)1, 47m, 48, 32 },
                    { (byte)1, 48m, 38, 26 },
                    { (byte)1, 49m, 19, 13 },
                    { (byte)1, 50m, 19, 13 },
                    { (byte)0, 70m, 7, 5 },
                    { (byte)3, 70m, 6, 4 },
                    { (byte)0, 71m, 17, 11 },
                    { (byte)3, 71m, 8, 6 },
                    { (byte)0, 72m, 29, 19 },
                    { (byte)3, 72m, 12, 8 },
                    { (byte)2, 73m, 10, 6 },
                    { (byte)2, 74m, 22, 14 },
                    { (byte)2, 75m, 36, 24 },
                    { (byte)0, 76m, 5, 3 },
                    { (byte)1, 77m, 24, 16 },
                    { (byte)2, 78m, 7, 5 },
                    { (byte)0, 79m, 11, 7 },
                    { (byte)1, 80m, 54, 36 },
                    { (byte)2, 81m, 16, 10 },
                    { (byte)0, 82m, 19, 13 },
                    { (byte)1, 83m, 96, 64 },
                    { (byte)2, 84m, 28, 18 },
                    { (byte)0, 85m, 23, 15 },
                    { (byte)0, 86m, 38, 26 },
                    { (byte)0, 87m, 31, 21 },
                    { (byte)0, 88m, 16, 10 },
                    { (byte)0, 89m, 16, 10 },
                    { (byte)1, 90m, 115, 77 },
                    { (byte)1, 91m, 192, 128 },
                    { (byte)1, 92m, 154, 102 },
                    { (byte)1, 93m, 77, 51 },
                    { (byte)1, 94m, 77, 51 },
                    { (byte)2, 95m, 32, 22 },
                    { (byte)2, 96m, 54, 36 },
                    { (byte)2, 97m, 43, 29 },
                    { (byte)2, 98m, 22, 14 },
                    { (byte)2, 99m, 22, 14 },
                    { (byte)4, 100m, 11, 7 },
                    { (byte)5, 100m, 19, 13 },
                    { (byte)6, 100m, 735, 665 },
                    { (byte)4, 101m, 14, 10 },
                    { (byte)5, 101m, 25, 17 },
                    { (byte)6, 101m, 945, 855 },
                    { (byte)4, 102m, 16, 10 },
                    { (byte)5, 102m, 26, 18 },
                    { (byte)6, 102m, 1050, 950 },
                    { (byte)4, 103m, 13, 9 },
                    { (byte)5, 103m, 20, 14 },
                    { (byte)6, 103m, 840, 760 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItemTemplateBaseStats");
        }
    }
}
