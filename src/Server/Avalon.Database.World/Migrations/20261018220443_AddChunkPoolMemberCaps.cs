using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class AddChunkPoolMemberCaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxPerLayout",
                table: "ChunkPoolMembership",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ChunkPoolMembership_MaxPerLayoutPositive",
                table: "ChunkPoolMembership",
                sql: "\"MaxPerLayout\" IS NULL OR \"MaxPerLayout\" >= 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ChunkPoolMembership_MaxPerLayoutPositive",
                table: "ChunkPoolMembership");

            migrationBuilder.DropColumn(
                name: "MaxPerLayout",
                table: "ChunkPoolMembership");
        }
    }
}
