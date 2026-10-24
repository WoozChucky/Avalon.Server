using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <summary>
    /// #1179: a base's defence moves from its template to its implicit modifier, so the 60 defence-bearing bases lose
    /// their stat lines (an armour piece's armour, evasion or ward; a shield's armour and block; a caster off-hand's ward
    /// and increased spell damage). Every copy rolls them as its implicit; the copies already owned get theirs at the
    /// drafted value from the Character migration AddImplicitsToOwnedItems. Down puts the lines back.
    /// </summary>
    public partial class MoveBaseStatsToImplicits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 12m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 13m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 14m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 15m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 16m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 17m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 18m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 19m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 20m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 21m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 22m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 23m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 24m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 25m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 26m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 36m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 37m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 38m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 39m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 40m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 41m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 42m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 43m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 44m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 45m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 46m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 47m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 48m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 49m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 50m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 70m,
                columns: new[] { "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 71m,
                columns: new[] { "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 72m,
                columns: new[] { "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 73m,
                columns: new[] { "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 74m,
                columns: new[] { "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 75m,
                columns: new[] { "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 76m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 77m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 78m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 79m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 80m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 81m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 82m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 83m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 84m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 85m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 86m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 87m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 88m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 89m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 90m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 91m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 92m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 93m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 94m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 95m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 96m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 97m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 98m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 99m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 12m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 4, 11L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 13m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 4, 18L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 14m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 4, 14L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 15m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 4, 7L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 16m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 4, 7L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 17m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 14, 16L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 18m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 14, 26L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 19m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 14, 21L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 20m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 14, 10L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 21m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 14, 10L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 22m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 6, 54L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 23m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 6, 90L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 24m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 6, 72L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 25m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 6, 36L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 26m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 6, 36L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 36m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 4, 5L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 37m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 4, 8L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 38m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 4, 6L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 39m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 4, 3L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 40m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 4, 3L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 41m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 14, 7L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 42m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 14, 12L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 43m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 14, 10L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 44m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 14, 5L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 45m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 14, 5L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 46m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 6, 24L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 47m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 6, 40L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 48m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 6, 32L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 49m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 6, 16L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 50m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 6, 16L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 70m,
                columns: new[] { "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { 4, 5, 6L, 5L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 71m,
                columns: new[] { "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { 4, 5, 14L, 7L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 72m,
                columns: new[] { "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { 4, 5, 24L, 10L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 73m,
                columns: new[] { "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { 14, 26, 8L, 5L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 74m,
                columns: new[] { "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { 14, 26, 18L, 8L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 75m,
                columns: new[] { "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { 14, 26, 30L, 12L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 76m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 4, 4L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 77m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 6, 20L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 78m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 14, 6L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 79m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 4, 9L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 80m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 6, 45L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 81m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 14, 13L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 82m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 4, 16L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 83m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 6, 80L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 84m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 14, 23L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 85m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 4, 19L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 86m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 4, 32L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 87m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 4, 26L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 88m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 4, 13L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 89m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 4, 13L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 90m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 6, 96L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 91m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 6, 160L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 92m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 6, 128L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 93m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 6, 64L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 94m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 6, 64L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 95m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 14, 27L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 96m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 14, 45L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 97m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 14, 36L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 98m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 14, 18L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 99m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 14, 18L });
        }
    }
}
