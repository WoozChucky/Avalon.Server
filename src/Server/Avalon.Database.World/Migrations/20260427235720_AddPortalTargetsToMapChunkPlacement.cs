using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class AddPortalTargetsToMapChunkPlacement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BackPortalTargetMapId",
                table: "MapChunkPlacements",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ForwardPortalTargetMapId",
                table: "MapChunkPlacements",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BackPortalTargetMapId",
                table: "MapChunkPlacements");

            migrationBuilder.DropColumn(
                name: "ForwardPortalTargetMapId",
                table: "MapChunkPlacements");
        }
    }
}
