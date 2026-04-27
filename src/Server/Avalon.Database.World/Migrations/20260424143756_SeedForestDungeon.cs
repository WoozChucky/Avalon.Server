using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class SeedForestDungeon : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "MapPortals",
                columns: new[] { "Id", "Radius", "SourceMapId", "TargetMapId", "X", "Y", "Z" },
                values: new object[] { 1, 3f, 1, 2, 50f, 51f, 50f });

            migrationBuilder.InsertData(
                table: "MapTemplates",
                columns: new[] { "Id", "AreaTableId", "CorpseX", "CorpseY", "CorpseZ", "DefaultSpawnX", "DefaultSpawnY", "DefaultSpawnZ", "Description", "Directory", "LoadingScreenId", "LogoutMapId", "MapType", "MaxLevel", "MaxPlayers", "MinLevel", "Name", "PvP" },
                values: new object[] { 2, 0, null, null, null, 0f, 0f, 0f, "Forest Dungeon", "Maps/", 0, 1, 1, 10, 1, 1, "ForestDungeon", false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "MapPortals",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "MapTemplates",
                keyColumn: "Id",
                keyValue: 2);
        }
    }
}
